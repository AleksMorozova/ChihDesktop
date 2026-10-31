namespace ChihDesktop;

public sealed record UserSettings
{
    public int SchemaVersion { get; init; } = 1;
    public string OwnerDisplayName { get; init; } = "";
    public string Language { get; init; } = "ru";
    public string TimeZoneId { get; init; } = TimeZoneInfo.Local.Id;
    public bool LaunchAtSignIn { get; init; }
    public bool StartPetOnly { get; init; } = true;
    public bool PetTopmost { get; init; } = true;
    public double CatSize { get; init; } = 160;
    public double WalkingSpeed { get; init; } = 70;
    public int AnimationFps { get; init; } = 30;
    public bool ReminderNotificationsEnabled { get; init; } = true;
    public PersonalityOptions Personality { get; init; } = new();
    public bool OnboardingCompleted { get; init; }

    public UserSettings Validate()
    {
        if (SchemaVersion != 1 || OwnerDisplayName is null || OwnerDisplayName.Trim().Length > 60 || OwnerDisplayName.Any(char.IsControl) || Language != "ru" ||
            !AppTimeZones.IsValid(TimeZoneId) || !double.IsFinite(CatSize) || CatSize is < 64 or > 400 || !double.IsFinite(WalkingSpeed) || WalkingSpeed is < 10 or > 500 ||
            AnimationFps is < 5 or > 60 || Personality is null || Personality.MinimumCommentIntervalMinutes is < 1 or > 1440)
            throw new ArgumentException("Проверьте имя (до 60 символов), часовой пояс и параметры Чиха.");
        return this with { OwnerDisplayName = OwnerDisplayName.Trim(), Personality = ReminderDataCodec.Copy(Personality) };
    }
}

public static class AppTimeZones
{
    public static TimeZoneInfo Get(string id) => id is "Europe/Kyiv" or "Europe/Kiev" ? Kyiv() : TimeZoneInfo.FindSystemTimeZoneById(id);
    private static TimeZoneInfo Kyiv()
    {
        foreach (var id in new[] { "Europe/Kyiv", "Europe/Kiev", "FLE Standard Time" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { }
        throw new TimeZoneNotFoundException("Kyiv rules unavailable.");
    }
    public static bool IsValid(string? id)
    { try { return !string.IsNullOrWhiteSpace(id) && Get(id) is not null; } catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException) { return false; } }
}

public sealed record SettingsLoadResult(UserSettings? Data, bool CanWrite, bool Existing, string? Error);
public interface IUserSettingsPersistence
{
    SettingsLoadResult Load();
    bool Save(UserSettings settings);
    string? Error { get; }
}
public interface IAutostartRegistration
{
    string? Read();
    void Write(string? command);
    string Command(bool petOnly);
}
public sealed record SettingsCommandResult(bool Success, string? Error);

public sealed class UserSettingsService
{
    private readonly IUserSettingsPersistence store;
    private readonly IAutostartRegistration autostart;
    private readonly object gate = new();
    private UserSettings current;
    private bool existing;
    private bool profileRecoveryRequired;
    public bool CanWrite { get; }
    public string? Error { get; private set; }
    public event Action? Changed;
    public bool ShouldOnboard => CanWrite && !profileRecoveryRequired && !GetSnapshot().OnboardingCompleted;
    public void DeferOnboardingForRecovery(string reason)
    {
        lock (gate) { profileRecoveryRequired = true; Error ??= reason; }
    }
    public SettingsCommandResult SaveOnboardingDraftIfNeeded()
    {
        UserSettings draft;
        lock (gate)
        {
            if (profileRecoveryRequired) return new(false, Error);
            if (persisted || existing || !CanWrite || current.OnboardingCompleted) return new(true, null);
            draft = current;
        }
        return Save(draft); // New file only; the completion flag remains false.
    }
    public UserSettingsService(IUserSettingsPersistence store, IAutostartRegistration autostart)
    {
        this.store = store; this.autostart = autostart;
        var load = store.Load(); current = load.Data ?? new(); CanWrite = load.CanWrite; Error = load.Error; existing = persisted = load.Existing;
    }
    public UserSettings GetSnapshot() { lock (gate) return ReminderDataCodec.Copy(current); }
    // In-memory compatibility import only. No old file is changed, and no onboarding is forced.
    public void AdoptExisting(ReminderData data)
    {
        lock (gate)
        {
            if (existing || !CanWrite) return;
            current = current with { TimeZoneId = data.TimeZoneId, Personality = ReminderDataCodec.Copy(data.Personality), OnboardingCompleted = true };
            existing = true;
        }
    }
    public void AdoptLegacyPet(bool topmost, double size, double speed, int fps)
    {
        lock (gate) if (current.OnboardingCompleted && !persisted) current = (current with { PetTopmost = topmost, CatSize = size, WalkingSpeed = speed, AnimationFps = fps }).Validate();
    }
    private bool persisted;
    public SettingsCommandResult Save(UserSettings edited)
    {
        string? previousCommand = null; bool registryChanged = false;
        lock (gate)
        {
            if (!CanWrite) return new(false, Error ?? "Настройки недоступны. Восстановите совместимый файл и перезапустите Чиха.");
            try
            {
                edited = edited.Validate();
                if (edited.LaunchAtSignIn != current.LaunchAtSignIn || edited.LaunchAtSignIn && edited.StartPetOnly != current.StartPetOnly)
                {
                    previousCommand = autostart.Read();
                    string? desiredCommand = edited.LaunchAtSignIn ? autostart.Command(edited.StartPetOnly) : null;
                    registryChanged = true;
                    autostart.Write(desiredCommand);
                }
                if (!store.Save(edited)) throw new IOException(store.Error ?? "Не удалось сохранить настройки.");
                current = ReminderDataCodec.Copy(edited); existing = persisted = true; Error = null;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException)
            {
                Error = "Не сохранено. " + ex.Message;
                if (registryChanged)
                    try { autostart.Write(previousCommand); }
                    catch (Exception rollback) when (rollback is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException)
                    { Error += " Не удалось восстановить автозапуск. Отключите запись ChihDesktop вручную в параметрах Windows."; }
                return new(false, Error);
            }
        }
        Changed?.Invoke(); return new(true, null);
    }
    public SettingsCommandResult CompleteOnboarding(bool explicitSkip) => Save(GetSnapshot() with { OnboardingCompleted = true });
}

public static class DialogueTemplate
{
    public static string Render(string template, string? ownerName) => template.Replace("{ownerName}", string.IsNullOrWhiteSpace(ownerName) ? "Хозяин" : ownerName.Trim(), StringComparison.Ordinal);
}
