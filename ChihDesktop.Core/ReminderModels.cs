namespace ChihDesktop;

public enum ReminderRecurrence { Daily, RunningInterval, Weekly }
public enum ReminderAcknowledgment { Pending, Dismissed, Completed }

public sealed class ReminderDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Напоминание";
    public TimeOnly Time { get; set; } = new(10, 0);
    public ReminderRecurrence Recurrence { get; set; } = ReminderRecurrence.Daily;
    public int IntervalMinutes { get; set; } = 2;
    public DayOfWeek WeeklyDay { get; set; } = DayOfWeek.Wednesday;
    public string BubbleGroup { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? TimeZoneId { get; set; }
    public bool Enabled { get; set; } = true;
    public bool RequiresConfirmation { get; set; } = true;
    public bool BypassQuietHours { get; set; }
    public List<string> MessageVariants { get; set; } = ["Мяу! Небольшое напоминание 🐾"];
    public DateOnly? LastFiredLocalDate { get; set; }
}

public sealed class ReminderOccurrence
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? TimeZoneId { get; set; }
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ReminderId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public DateOnly LocalDate { get; set; }
    public DateTimeOffset DueAtUtc { get; set; }
    public bool RequiresConfirmation { get; set; }
    public bool IsManualTest { get; set; }
    public string PreviewBatchId { get; set; } = "";
    public string BubbleGroup { get; set; } = "";
    public bool IsLegacyHistory { get; set; }
    public ReminderAcknowledgment Acknowledgment { get; set; }
    public DateTimeOffset? AcknowledgedAtUtc { get; set; }
    public DateTimeOffset? NotifiedAtUtc { get; set; }
    public DateTimeOffset? SnoozedUntilUtc { get; set; }
}

public sealed class ReminderData
{
    public int Version { get; set; } = 3;
    public string TimeZoneId { get; set; } = "Europe/Kyiv";
    public bool QuietHoursEnabled { get; set; } = true;
    public TimeOnly QuietStart { get; set; } = new(22, 0);
    public TimeOnly QuietEnd { get; set; } = new(8, 0);
    public PersonalityOptions Personality { get; set; } = new();
    public List<ReminderDefinition> Reminders { get; set; } = [];
    public List<ReminderOccurrence> Occurrences { get; set; } = [];

    public static ReminderData CreateDefaults() => new() { TimeZoneId = TimeZoneInfo.Local.Id };
}

