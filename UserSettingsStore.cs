using System.IO;
using System.Text;
using System.Text.Json;

namespace ChihDesktop;

public class UserSettingsStore(string? path = null, Func<UserSettings>? defaults = null) : IUserSettingsPersistence
{
    private static readonly JsonSerializerOptions Options = new(ReminderDataCodec.JsonOptions)
    { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    public string FilePath { get; } = Path.GetFullPath(path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChihDesktop", "user-settings.json"));
    private enum State { Missing, Valid, Corrupt, Unsupported, Io }
    private sealed record Candidate(State State, byte[]? Bytes = null, UserSettings? Data = null, int Version = 1);
    private bool writable;
    public string? Error { get; private set; }
    private static Candidate Read(string path)
    {
        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return new(State.Missing); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new(State.Io); }
        try
        {
            using var stream = new MemoryStream(bytes);
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
            string json = reader.ReadToEnd();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("SchemaVersion", out var schema) || !schema.TryGetInt32(out int version)) throw new JsonException();
            if (version is not (0 or 1)) return new(State.Unsupported, bytes);
            var data = JsonSerializer.Deserialize<UserSettings>(json, Options) ?? throw new JsonException();
            // v0 draft used the same fields; normalize only in memory, never rewrite on load.
            data = (data with { SchemaVersion = 1 }).Validate();
            return new(State.Valid, bytes, data, version);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or DecoderFallbackException) { return new(State.Corrupt, bytes); }
    }
    private static void Preserve(string path, Candidate candidate)
    {
        if (candidate.State != State.Corrupt) return;
        using var file = new FileStream(path + ".invalid-" + Guid.NewGuid().ToString("N"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.Write(candidate.Bytes!); file.Flush(true);
    }
    public SettingsLoadResult Load()
    {
        var primary = Read(FilePath); var backup = Read(FilePath + ".bak");
        try
        {
            Preserve(FilePath, primary); Preserve(FilePath + ".bak", backup);
            if (primary.State is State.Unsupported or State.Io) return Block(primary.State);
            if (primary.State == State.Valid) { writable = true; return new(primary.Data, true, true, null); }
            if (backup.State == State.Valid) { writable = true; Error = "Настройки восстановлены из проверенной копии. Проверьте последние изменения."; return new(backup.Data, true, true, Error); }
            if (primary.State == State.Missing && backup.State == State.Missing)
            { writable = true; return new((defaults?.Invoke() ?? new UserSettings()).Validate(), true, false, null); }
            return Block(backup.State);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Block(State.Io); }
    }
    private SettingsLoadResult Block(State state)
    {
        writable = false;
        Error = state == State.Unsupported ? "Настройки созданы другой версией. Обновите приложение; файл не изменён." : "Нет проверенной копии настроек или доступа к ней. Файлы сохранены. Восстановите свою копию и перезапустите Чиха.";
        return new(null, false, true, Error);
    }
    protected virtual void Commit(string temporary, string destination)
    {
        if (File.Exists(destination)) File.Replace(temporary, destination, null);
        else File.Move(temporary, destination);
    }
    private void AtomicWrite(string destination, byte[] bytes)
    {
        string temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) { file.Write(bytes); file.Flush(true); }
            var verified = Read(temporary);
            if (verified.State != State.Valid || !verified.Bytes!.SequenceEqual(bytes)) throw new IOException("Не удалось проверить временный файл настроек.");
            Commit(temporary, destination);
        }
        finally { try { File.Delete(temporary); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }
    }
    public bool Save(UserSettings settings)
    {
        if (!writable) { Error ??= "Хранилище настроек заблокировано."; return false; }
        try
        {
            settings = settings.Validate();
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var primary = Read(FilePath); var backup = Read(FilePath + ".bak");
            if (primary.State is State.Unsupported or State.Io || backup.State == State.Io) throw new IOException("Проверьте версию приложения и доступ к файлам.");
            Preserve(FilePath, primary); Preserve(FilePath + ".bak", backup);
            if (primary.State != State.Valid && backup is { State: State.Valid, Version: 0 }) PreserveDraft(backup.Bytes!);
            if (primary.State == State.Valid)
            {
                if (primary.Version == 0) PreserveDraft(primary.Bytes!);
                if (backup.State != State.Unsupported) AtomicWrite(FilePath + ".bak", primary.Bytes!);
                else AtomicWrite(FilePath + ".compatible-" + Guid.NewGuid().ToString("N") + ".bak", primary.Bytes!);
            }
            AtomicWrite(FilePath, JsonSerializer.SerializeToUtf8Bytes(settings, ReminderDataCodec.JsonOptions));
            Error = null; return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        { Error = "Не удалось сохранить настройки. Проверьте доступ, свободное место и версию приложения. " + ex.Message; return false; }
    }
    private void PreserveDraft(byte[] bytes)
    {
        string path = FilePath + ".schema-v0-backup";
        if (!File.Exists(path)) AtomicWrite(path, bytes);
        else if (Read(path).State != State.Valid) throw new IOException("Постоянная копия настроек непригодна. Восстановите её перед сохранением.");
    }
}
