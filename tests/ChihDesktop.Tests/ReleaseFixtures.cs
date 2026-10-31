namespace ChihDesktop.Tests;

internal static class SyntheticReminderFixtures
{
    // Deliberately fictional opt-in test schedule, never referenced by production defaults.
    public static ReminderData Schedule() => new()
    {
        Version = 2, TimeZoneId = "Europe/Kyiv", QuietStart = new(23, 0), QuietEnd = new(9, 0),
        Reminders =
        [
            new() { Id = "test-break", Title = "Пример паузы", Recurrence = ReminderRecurrence.RunningInterval, RequiresConfirmation = false, IntervalMinutes = 2 },
            Item("sample-a", "Пример A", 12, 30),
            Item("sample-b", "Пример B", 15, 0, "afternoon"), Item("lunch", "Пример обеда", 15, 0, "afternoon"),
            Item("sample-c", "Пример C", 21, 0, "evening"), Item("dinner", "Пример ужина", 21, 0, "evening"),
            new() { Id = "sample-weekly", Title = "Еженедельный пример", Time = new(17, 0), Recurrence = ReminderRecurrence.Weekly, WeeklyDay = DayOfWeek.Wednesday }
        ]
    };
    private static ReminderDefinition Item(string id, string title, int hour, int minute, string group = "") => new() { Id = id, Title = title, Time = new(hour, minute), BubbleGroup = group };
}

internal static class TestSettings
{
    public static UserSettingsService Create() => new(new MemorySettings { Data = new UserSettings { TimeZoneId = "Europe/Kyiv" } }, new MemoryAutostart());
}
internal sealed class MemorySettings : IUserSettingsPersistence
{
    public UserSettings Data = new(); public bool Existing, Fail; public int Saves;
    public string? Error => Fail ? "Synthetic write failure" : null;
    public SettingsLoadResult Load() => new(ReminderDataCodec.Copy(Data), true, Existing, null);
    public bool Save(UserSettings settings) { Saves++; if (Fail) return false; Data = ReminderDataCodec.Copy(settings); Existing = true; return true; }
}
internal sealed class MemoryAutostart : IAutostartRegistration
{
    public string? Value; public bool Fail; public int Writes;
    public string? Read() => Value;
    public void Write(string? command) { if (Fail) throw new IOException("Synthetic registry failure"); Value = command; Writes++; }
    public string Command(bool petOnly) => "\"synthetic-app.exe\"" + (petOnly ? " --pet-only" : "");
}
