using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChihDesktop;


public class ReminderStore : IReminderPersistence
{
    private readonly TimeProvider clock;
    public static readonly JsonSerializerOptions JsonOptions = ReminderDataCodec.JsonOptions;
    private enum FileState { Missing, Valid, Corrupt, Unsupported, Unreadable }
    private sealed record Candidate(FileState State, ReminderData? Data = null, byte[]? Bytes = null, string? Detail = null);
    private bool writable;
    public string FilePath { get; }
    public string? Error { get; private set; }
    public bool CanWrite => writable;
    public ReminderLoadResult? LoadOutcome { get; private set; }

    public ReminderStore(string? path = null, TimeProvider? clock = null)
    {
        this.clock = clock ?? TimeProvider.System;
        FilePath = Path.GetFullPath(path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChihDesktop", "reminders.json"));
    }

    internal static T Copy<T>(T value) => ReminderDataCodec.Copy(value);
    internal static void Validate(ReminderData data) => ReminderDataCodec.Validate(data);

    private static Candidate Read(string path)
    {
        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return new(FileState.Missing); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new(FileState.Unreadable, Detail: ex.Message); }
        try
        {
            using var input = new MemoryStream(bytes);
            using var reader = new StreamReader(input, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
            string json = reader.ReadToEnd(); // Accept BOM-bearing files saved by Windows editors.
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("Version", out var version) || !version.TryGetInt32(out int number))
                throw new JsonException("Missing schema Version.");
            // Inspect version before deserializing fields from a potentially different schema.
            if (number is not (1 or 2 or 3)) return new(FileState.Unsupported, Bytes: bytes, Detail: $"Version={number}");
            var data = JsonSerializer.Deserialize<ReminderData>(json, JsonOptions) ?? throw new JsonException("Empty file.");
            if (number is 1 or 2) data.TimeZoneId = "Europe/Kyiv"; // Historical semantics; no file rewrite on load.
            Validate(data);
            return new(FileState.Valid, data, bytes);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException or DecoderFallbackException)
        { return new(FileState.Corrupt, Bytes: bytes, Detail: ex.Message); }
    }

    private void PreserveCorrupt(string path, Candidate file)
    {
        if (file.State != FileState.Corrupt) return;
        string diagnostic = path + ".invalid-" + clock.GetUtcNow().ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N");
        // Preserve the exact bytes inspected, without interpreting or logging personal content.
        using var output = new FileStream(diagnostic, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        output.Write(file.Bytes!); output.Flush(true);
    }

    public ReminderLoadResult Load()
    {
        var primary = Read(FilePath);
        var backup = Read(FilePath + ".bak"); // Always inspect both before selecting either.
        ReminderLoadResult result;
        try
        {
            PreserveCorrupt(FilePath, primary);
            PreserveCorrupt(FilePath + ".bak", backup);
            if (primary.State == FileState.Unsupported)
                result = new(ReminderLoadStatus.BlockedUnsupported, null,
                    $"Файл напоминаний создан другой версией ({primary.Detail}). Обновите приложение. Файлы не изменены; напоминания приостановлены.", false);
            else if (primary.State == FileState.Unreadable)
                result = new(ReminderLoadStatus.BlockedIo, null, "Нет доступа к файлу напоминаний. Проверьте права и доступность папки: " + FilePath, false);
            else if (primary.State == FileState.Valid)
                result = new(ReminderLoadStatus.Primary, primary.Data,
                    backup.State == FileState.Valid || backup.State == FileState.Missing ? null : "Резервный файл непригоден. Использован проверенный основной файл; резерв другой версии не перезаписывается.", true);
            else if (backup.State == FileState.Valid)
                result = new(ReminderLoadStatus.RecoveredBackup, backup.Data,
                    "Напоминания восстановлены из проверенной резервной копии. Проверьте последние отметки; повреждённый файл сохранён для диагностики.", true);
            else if (primary.State == FileState.Missing && backup.State == FileState.Missing)
                result = new(ReminderLoadStatus.NewStore, ReminderData.CreateDefaults(), null, true);
            else
                result = new(backup.State == FileState.Unsupported ? ReminderLoadStatus.BlockedUnsupported :
                    backup.State == FileState.Unreadable ? ReminderLoadStatus.BlockedIo : ReminderLoadStatus.BlockedCorrupt, null,
                    "Нет совместимой проверенной копии напоминаний. Файлы сохранены; автоматический сброс отключён. Восстановите файл из своей копии и перезапустите приложение. Папка: " + Path.GetDirectoryName(FilePath), false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result = new(ReminderLoadStatus.BlockedIo, null,
                "Не удалось сохранить диагностическую копию. Исправьте доступ к папке и перезапустите приложение. " + ex.Message, false);
        }
        writable = result.CanWrite;
        Error = writable ? null : result.Message;
        return LoadOutcome = result;
    }

    // Test seam at the actual atomic commit boundary; no production failure flags.
    protected virtual void CommitFile(string temporary, string destination)
    {
        if (File.Exists(destination)) File.Replace(temporary, destination, null);
        else File.Move(temporary, destination);
    }

    private void AtomicWrite(string path, byte[] bytes)
    {
        string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { output.Write(bytes); output.Flush(true); }
            var verified = Read(temporary);
            if (verified.State != FileState.Valid || !verified.Bytes!.SequenceEqual(bytes)) throw new IOException("Temporary file failed validation.");
            CommitFile(temporary, path);
        }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    public bool Save(ReminderData data)
    {
        if (!writable) { Error ??= "Хранилище недоступно для записи. Исправьте файл и перезапустите приложение."; return false; }
        try
        {
            Validate(data);
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data, JsonOptions));
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var primary = Read(FilePath);
            var backup = Read(FilePath + ".bak");
            if (primary.State is FileState.Unsupported or FileState.Unreadable)
                throw new IOException("Основной файл изменён другой версией или недоступен. Перезапустите приложение после проверки файла.");
            PreserveCorrupt(FilePath, primary);
            PreserveCorrupt(FilePath + ".bak", backup);
            if (primary.State != FileState.Valid && backup is { State: FileState.Valid, Data.Version: < 3 }) PreserveLegacy(backup.Bytes!);
            // Only a validated primary may rotate into backup. A recovered valid backup is left intact.
            // Never overwrite a backup whose schema this application cannot understand.
            if (primary.State != FileState.Valid && backup is { State: FileState.Valid, Data.Version: 1 } &&
                !File.Exists(FilePath + ".schedule-v1-backup"))
                AtomicWrite(FilePath + ".schedule-v1-backup", backup.Bytes!);
            if (primary.State == FileState.Valid)
            {
                if (primary.Data!.Version < 3) PreserveLegacy(primary.Bytes!);
                if (primary.Data!.Version == 1 && !File.Exists(FilePath + ".schedule-v1-backup"))
                    AtomicWrite(FilePath + ".schedule-v1-backup", primary.Bytes!);
                if (backup.State == FileState.Unreadable) throw new IOException("Нет доступа к резервному файлу.");
                if (backup.State != FileState.Unsupported) AtomicWrite(FilePath + ".bak", primary.Bytes!);
                else AtomicWrite(FilePath + ".compatible-" + Guid.NewGuid().ToString("N") + ".bak", primary.Bytes!);
            }
            AtomicWrite(FilePath, bytes);
            Error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Error = "Не сохранено. Изменение отменено. Проверьте свободное место, права доступа и резервный файл; затем повторите действие. " + ex.Message;
            return false;
        }
    }
    private void PreserveLegacy(byte[] bytes)
    {
        string path = FilePath + ".pre-v03-backup";
        if (!File.Exists(path)) AtomicWrite(path, bytes);
        else if (Read(path).State != FileState.Valid) throw new IOException("Постоянная резервная копия непригодна. Восстановите её перед сохранением.");
    }
}

