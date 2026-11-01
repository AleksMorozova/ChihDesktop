using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ChihDesktop;

// Explicit DTO boundary over the same singleton services used by native WPF.
// The WebView host invokes these synchronous commands on its Dispatcher thread.
// Never expose file paths, arbitrary settings properties, history mutation or EF entities.
public sealed class AdminApplicationApi(IReminderCommands reminders, UserSettingsService settings, TimeProvider clock)
{
    internal static IReadOnlyList<string> Operations { get; } = Array.AsReadOnly(new[] { "reminders.list", "reminders.create", "reminders.update", "reminders.enable", "reminders.delete", "reminders.snooze", "reminders.acknowledge", "settings.get", "settings.save" });
    public sealed record ReminderInput
    {
        public required string Title { get; init; }
        public required string Time { get; init; }
        public required ReminderRecurrence Recurrence { get; init; }
        public required int IntervalMinutes { get; init; }
        public required DayOfWeek WeeklyDay { get; init; }
        public required bool Enabled { get; init; }
        public required bool BypassQuietHours { get; init; }
    }
    public sealed record SettingsInput
    {
        public required string OwnerDisplayName { get; init; }
        public required string TimeZoneId { get; init; }
        public required bool LaunchAtSignIn { get; init; }
        public required bool StartPetOnly { get; init; }
        public required bool PetTopmost { get; init; }
        public required bool ReminderNotificationsEnabled { get; init; }
        public bool TaskReminderNotificationsEnabled { get; init; } = true;
        public bool TaskSummarySpeechEnabled { get; init; }
        public bool ShowTaskTitlesInBubbles { get; init; } = true;
        public bool TaskCompletionReactionsEnabled { get; init; } = true;
        public required bool PersonalityEnabled { get; init; }
        public required bool SpontaneousCommentsEnabled { get; init; }
        public required int MinimumCommentIntervalMinutes { get; init; }
        public required bool QuietHoursEnabled { get; init; }
        public required string QuietStart { get; init; }
        public required string QuietEnd { get; init; }
    }
    public sealed record ReminderDto(string Id, string Version, ReminderInput Definition, string TimeZoneId);
    public sealed record OccurrenceDto(string Id, string ReminderId, string Title, DateOnly LocalDate, DateTimeOffset DueAtUtc,
        ReminderAcknowledgment Acknowledgment, DateTimeOffset? SnoozedUntilUtc, bool IsManualTest);
    private sealed record Empty;
    private sealed record Create { public required ReminderInput Reminder { get; init; } }
    private sealed record Edit { public required string Id { get; init; } public required string ExpectedVersion { get; init; } public required ReminderInput Reminder { get; init; } }
    private sealed record Enable { public required string Id { get; init; } public required string ExpectedVersion { get; init; } public required bool Enabled { get; init; } }
    private sealed record Identity { public required string Id { get; init; } }
    private sealed record Delete { public required string Id { get; init; } public required string ExpectedVersion { get; init; } }
    private sealed record Acknowledge { public required string Id { get; init; } public required bool Completed { get; init; } }
    private sealed record SaveSettings { public required string ExpectedVersion { get; init; } public required SettingsInput Settings { get; init; } }

    public object Execute(string operation, JsonElement payload) => operation switch
    {
        "reminders.list" => List(Read<Empty>(payload)),
        "reminders.create" => CreateReminder(Read<Create>(payload)),
        "reminders.update" => EditReminder(Read<Edit>(payload)),
        "reminders.enable" => EnableReminder(Read<Enable>(payload)),
        "reminders.delete" => DeleteReminder(Read<Delete>(payload)),
        "reminders.snooze" => Result(reminders.Snooze(Id(Read<Identity>(payload).Id))),
        "reminders.acknowledge" => Ack(Read<Acknowledge>(payload)),
        "settings.get" => GetSettings(Read<Empty>(payload)),
        "settings.save" => Save(Read<SaveSettings>(payload)),
        _ => throw new JsonException()
    };
    private static T Read<T>(JsonElement payload) where T : class => payload.ValueKind == JsonValueKind.Object
        ? payload.Deserialize<T>(AdminBridge.JsonOptions) ?? throw new JsonException() : throw new JsonException();
    private static string Id(string id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 200 ? id : throw new JsonException();
    private static string Version(object data) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data, AdminBridge.JsonOptions))));
    private static TimeOnly Time(string value) => TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : throw new JsonException();
    private static bool Result(ReminderCommandResult result) => result.Success ? true : throw new AdminCommandException("PersistenceOrValidation", result.Error ?? "Не сохранено. Обновите список.");
    private static void Match(string expected, object data)
    {
        if (expected is null || expected.Length != 64 || expected.Any(c => !char.IsAsciiHexDigit(c))) throw new JsonException();
        if (expected != Version(data)) throw new AdminCommandException("Conflict", "Данные изменились в другом окне. Обновите страницу перед сохранением.");
    }
    private static ReminderInput Input(ReminderDefinition r) => new() { Title = r.Title, Time = r.Time.ToString("HH:mm"), Recurrence = r.Recurrence,
        IntervalMinutes = r.IntervalMinutes, WeeklyDay = r.WeeklyDay, Enabled = r.Enabled, BypassQuietHours = r.BypassQuietHours };
    private static object IdentityVersion(ReminderDefinition r) => new { r.Id, Input = Input(r), r.TimeZoneId };
    private static ReminderDto Dto(ReminderDefinition r, string baseZone) => new(r.Id, Version(IdentityVersion(r)), Input(r), r.TimeZoneId ?? baseZone);
    private ReminderDefinition Current(string id, string expected)
    {
        _ = Id(id);
        var current = reminders.GetSnapshot().Reminders.FirstOrDefault(r => r.Id == id) ?? throw new AdminCommandException("NotFound", "Напоминание больше не существует. Обновите список.");
        Match(expected, IdentityVersion(current)); return current;
    }
    private static ReminderDefinition Apply(ReminderDefinition r, ReminderInput input)
    {
        if (input is null || string.IsNullOrWhiteSpace(input.Title) || input.Title.Length > 200 || input.IntervalMinutes is < 1 or > 1440 || !Enum.IsDefined(input.Recurrence) || !Enum.IsDefined(input.WeeklyDay)) throw new JsonException();
        r.Title = input.Title.Trim(); r.Time = Time(input.Time); r.Recurrence = input.Recurrence; r.IntervalMinutes = input.IntervalMinutes;
        r.WeeklyDay = input.WeeklyDay; r.Enabled = input.Enabled; r.BypassQuietHours = input.BypassQuietHours; return r;
    }
    private object List(Empty _)
    {
        var snapshot = reminders.GetSnapshot(); var today = reminders.LocalDate(clock.GetUtcNow());
        var occurrences = snapshot.Occurrences.Where(o => o.LocalDate == today || o.Acknowledgment == ReminderAcknowledgment.Pending).OrderByDescending(o => o.DueAtUtc).ToArray();
        return new { Definitions = snapshot.Reminders.Select(r => Dto(r, snapshot.TimeZoneId)).ToArray(),
            Occurrences = occurrences.Take(500).Select(o => new OccurrenceDto(o.Id, o.ReminderId, o.Title, o.LocalDate, o.DueAtUtc, o.Acknowledgment, o.SnoozedUntilUtc, o.IsManualTest)).ToArray(),
            Truncated = occurrences.Length > 500, BaseTimeZone = snapshot.TimeZoneId, Error = reminders.PersistenceError ?? reminders.StartupWarning };
    }
    private object CreateReminder(Create payload)
    {
        var definition = Apply(new ReminderDefinition { Enabled = false }, payload.Reminder);
        Result(reminders.CreateReminder(definition));
        var snapshot = reminders.GetSnapshot(); return Dto(snapshot.Reminders.Single(r => r.Id == definition.Id), snapshot.TimeZoneId);
    }
    private object EditReminder(Edit payload)
    {
        var current = Current(payload.Id, payload.ExpectedVersion);
        if (payload.Reminder is null || current.Recurrence != payload.Reminder.Recurrence) throw new JsonException();
        Result(reminders.UpdateReminder(Apply(current, payload.Reminder)));
        var snapshot = reminders.GetSnapshot(); return Dto(snapshot.Reminders.Single(r => r.Id == payload.Id), snapshot.TimeZoneId);
    }
    private bool EnableReminder(Enable payload) { _ = Current(payload.Id, payload.ExpectedVersion); return Result(reminders.SetReminderEnabled(payload.Id, payload.Enabled)); }
    private bool DeleteReminder(Delete payload) { _ = Current(payload.Id, payload.ExpectedVersion); return Result(reminders.DeleteReminder(payload.Id)); }
    private bool Ack(Acknowledge payload) => Result(reminders.Acknowledge(Id(payload.Id), payload.Completed));
    private static SettingsInput Input(UserSettings s) => new() { OwnerDisplayName = s.OwnerDisplayName, TimeZoneId = s.TimeZoneId,
        LaunchAtSignIn = s.LaunchAtSignIn, StartPetOnly = s.StartPetOnly, PetTopmost = s.PetTopmost, ReminderNotificationsEnabled = s.ReminderNotificationsEnabled,
        TaskReminderNotificationsEnabled = s.TaskReminderNotificationsEnabled, TaskSummarySpeechEnabled = s.TaskSummarySpeechEnabled, ShowTaskTitlesInBubbles = s.ShowTaskTitlesInBubbles, TaskCompletionReactionsEnabled = s.TaskCompletionReactionsEnabled, PersonalityEnabled = s.Personality.PersonalityEnabled, SpontaneousCommentsEnabled = s.Personality.SpontaneousCommentsEnabled,
        MinimumCommentIntervalMinutes = s.Personality.MinimumCommentIntervalMinutes, QuietHoursEnabled = s.Personality.QuietHoursEnabled,
        QuietStart = s.Personality.QuietStart.ToString("HH:mm"), QuietEnd = s.Personality.QuietEnd.ToString("HH:mm") };
    private object GetSettings(Empty _)
    {
        var current = settings.GetSnapshot();
        var zones = TimeZoneInfo.GetSystemTimeZones().Select(t => new { t.Id, Label = t.DisplayName }).ToList();
        if (!zones.Any(z => z.Id == current.TimeZoneId)) zones.Insert(0, new { Id = current.TimeZoneId, Label = current.TimeZoneId });
        return new { Settings = Input(current), Version = Version(current), settings.CanWrite, settings.Error, TimeZones = zones };
    }
    private object Save(SaveSettings payload)
    {
        var current = settings.GetSnapshot(); Match(payload.ExpectedVersion, current);
        var input = payload.Settings ?? throw new JsonException();
        var personality = ReminderDataCodec.Copy(current.Personality);
        personality.PersonalityEnabled = input.PersonalityEnabled; personality.SpontaneousCommentsEnabled = input.SpontaneousCommentsEnabled;
        personality.MinimumCommentIntervalMinutes = input.MinimumCommentIntervalMinutes; personality.QuietHoursEnabled = input.QuietHoursEnabled;
        personality.QuietStart = Time(input.QuietStart); personality.QuietEnd = Time(input.QuietEnd);
        var result = settings.Save(current with { OwnerDisplayName = input.OwnerDisplayName, TimeZoneId = input.TimeZoneId, LaunchAtSignIn = input.LaunchAtSignIn,
            StartPetOnly = input.StartPetOnly, PetTopmost = input.PetTopmost, ReminderNotificationsEnabled = input.ReminderNotificationsEnabled, TaskReminderNotificationsEnabled = input.TaskReminderNotificationsEnabled, TaskSummarySpeechEnabled = input.TaskSummarySpeechEnabled, ShowTaskTitlesInBubbles = input.ShowTaskTitlesInBubbles, TaskCompletionReactionsEnabled = input.TaskCompletionReactionsEnabled, Personality = personality });
        if (!result.Success) throw new AdminCommandException("PersistenceOrValidation", result.Error ?? "Не сохранено.");
        return GetSettings(new());
    }
}

public sealed class AdminCommandException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
