namespace ChihDesktop;

public sealed record ReminderCommandResult(bool Success, string? Error);

// Application-facing boundary. Returned models are detached copies, never scheduler collections.
// Commands resolve stable IDs and publish changes only after the scheduler has saved or rolled back.
public sealed class ReminderCommands : IReminderCommands
{
    private readonly ReminderScheduler scheduler;
    private readonly object gate = new();
    private bool changed;
    private string[] presentedIds = [];
    public event Action? Changed;
    public event Action<string>? OperationFailed;
    public string? PersistenceError { get; private set; }
    public string? StartupWarning => scheduler.StartupWarning;
    public ReminderLoadStatus LoadStatus { get; }
    public string ConfigurationPath => scheduler.ConfigurationPath;

    private readonly TimeProvider clock;
    private readonly UserSettingsService? userSettings;
    private string? personalityError;

    public ReminderCommands(IReminderPersistence store, TimeProvider? clock = null, UserSettingsService? userSettings = null)
    {
        this.clock = clock ?? TimeProvider.System;
        this.userSettings = userSettings;
        scheduler = new ReminderScheduler(store, timeProvider: this.clock, notificationsEnabled: () => userSettings is null || userSettings.CanWrite && userSettings.GetSnapshot().ReminderNotificationsEnabled);
        if (store.LoadOutcome?.Status != ReminderLoadStatus.NewStore && store.LoadOutcome?.Data is not null) userSettings?.AdoptExisting(scheduler.Data);
        if (store.LoadOutcome is { CanWrite: false, Data: null } blocked)
            userSettings?.DeferOnboardingForRecovery(blocked.Message ?? "Восстановите напоминания перед первым запуском настроек.");
        LoadStatus = store.LoadOutcome!.Status;
        PersistenceError = scheduler.PersistenceError;
        scheduler.Changed += () => changed = true;
    }

    // Convenience commands use the injected clock; existing explicit-instant overloads stay compatible.
    public ReminderCommandResult Acknowledge(string id, bool completed) => Acknowledge(id, completed, clock.GetUtcNow());
    public ReminderCommandResult Snooze(string id) => Snooze(id, clock.GetUtcNow());

    public ReminderData GetSnapshot()
    {
        lock (gate)
        {
            var snapshot = ReminderDataCodec.Copy(scheduler.Data);
            snapshot.Personality = GetPersonalityOptions();
            return snapshot;
        }
    }
    public PersonalityOptions GetPersonalityOptions() { lock (gate) return ReminderDataCodec.Copy(userSettings is { CanWrite: true } ? userSettings.GetSnapshot().Personality : scheduler.Data.Personality); }
    public DateTime LocalNow(DateTimeOffset now) { lock (gate) return scheduler.LocalNow(now); }
    public DateOnly LocalDate(DateTimeOffset now) { lock (gate) return scheduler.LocalDate(now); }
    public bool IsQuiet(DateTimeOffset now) { lock (gate) return scheduler.IsQuiet(now); }
    public bool MayNotify(ReminderOccurrence occurrence, DateTimeOffset now)
    { lock (gate) return Find(occurrence.Id) is { } actual && scheduler.MayNotify(actual, now); }
    public bool HasWaitingNotification(DateTimeOffset now)
    { lock (gate) return scheduler.Data.Occurrences.Any(o => o.NotifiedAtUtc is null && scheduler.MayNotify(o, now)); }
    private ReminderOccurrence? Find(string id) => scheduler.Data.Occurrences.FirstOrDefault(o => o.Id == id);

    private ReminderCommandResult Execute(Func<bool> action, string invalid = "Пункт не найден или действие уже недоступно. Обновите список.")
    {
        bool success, notify;
        string? error;
        lock (gate)
        {
            changed = false;
            personalityError = null;
            success = action();
            PersistenceError = success ? scheduler.PersistenceError : personalityError ?? scheduler.PersistenceError ?? invalid;
            error = PersistenceError;
            notify = changed || !success;
        }
        // Events run outside the state lock. Presentation code marshals to its Dispatcher if necessary.
        if (notify) Changed?.Invoke();
        if (!success) OperationFailed?.Invoke(error!);
        return new(success, error);
    }

    public void Tick(DateTimeOffset now, TimeSpan elapsed) => Execute(() =>
    { scheduler.Tick(now, elapsed); return scheduler.PersistenceError is null; });

    public ReminderCommandResult Acknowledge(string id, bool completed, DateTimeOffset now) => Execute(() =>
        Find(id) is { } occurrence && scheduler.Acknowledge(occurrence, completed, now));
    public ReminderCommandResult Snooze(string id, DateTimeOffset now) => Execute(() =>
        Find(id) is { } occurrence && scheduler.Snooze(occurrence, now));

    public ReminderCommandResult UpdateSettings(IReadOnlyList<ReminderDefinition> edits, bool quietEnabled, TimeOnly start, TimeOnly end)
        => Execute(() =>
        {
            if (edits.Count != scheduler.Data.Reminders.Count || edits.Select(r => r.Id).Distinct().Count() != edits.Count ||
                edits.Any(r => !scheduler.Data.Reminders.Any(x => x.Id == r.Id) || string.IsNullOrWhiteSpace(r.Title) || r.IntervalMinutes is < 1 or > 1440)) return false;
            if (edits.Any(edit =>
            {
                var current = scheduler.Data.Reminders.First(r => r.Id == edit.Id);
                return edit.TimeZoneId != current.TimeZoneId || edit.Recurrence != current.Recurrence || !Enum.IsDefined(edit.WeeklyDay);
            })) return false;
            // Apply only editable preferences, never recurrence identity, history or LastFiredLocalDate.
            foreach (var edit in edits)
            {
                var target = scheduler.Data.Reminders.First(r => r.Id == edit.Id);
                target.Title = edit.Title.Trim(); target.Time = edit.Time; target.IntervalMinutes = edit.IntervalMinutes;
                target.Enabled = edit.Enabled; target.BypassQuietHours = edit.BypassQuietHours; target.WeeklyDay = edit.WeeklyDay;
            }
            scheduler.Data.QuietHoursEnabled = quietEnabled; scheduler.Data.QuietStart = start; scheduler.Data.QuietEnd = end;
            return scheduler.SettingsChanged();
        });

    public ReminderCommandResult UpdatePersonality(PersonalityOptions options) => Execute(() =>
    {
        if (options.MinimumCommentIntervalMinutes is < 1 or > 1440) return false;
        if (userSettings is not null)
        {
            var result = userSettings.Save(userSettings.GetSnapshot() with { Personality = options });
            if (!result.Success) { personalityError = result.Error; return false; }
            changed = true; return true;
        }
        scheduler.Data.Personality = ReminderDataCodec.Copy(options);
        return scheduler.SavePreferences();
    });

    public ReminderCommandResult CreateReminder(ReminderDefinition definition) => Execute(() =>
    {
        if (userSettings is { CanWrite: false }) { personalityError = userSettings.Error; return false; }
        if (string.IsNullOrWhiteSpace(definition.Title) || definition.Title.Trim().Length > 200 || scheduler.Data.Reminders.Any(r => r.Id == definition.Id) || scheduler.Data.Occurrences.Any(o => o.ReminderId == definition.Id)) return false;
        var copy = ReminderDataCodec.Copy(definition);
        copy.Title = copy.Title.Trim();
        copy.LastFiredLocalDate = null;
        copy.TimeZoneId = userSettings?.GetSnapshot().TimeZoneId ?? scheduler.Data.TimeZoneId;
        var proposed = ReminderDataCodec.Copy(scheduler.Data);
        proposed.Reminders.Add(copy); proposed.Version = 3;
        try { ReminderDataCodec.Validate(proposed); } catch (System.Text.Json.JsonException) { return false; }
        scheduler.Data.Reminders.Add(copy); scheduler.Data.Version = 3;
        return scheduler.SettingsChanged();
    });

    public ReminderCommandResult DeleteReminder(string id) => Execute(() =>
    {
        var definition = scheduler.Data.Reminders.FirstOrDefault(r => r.Id == id);
        if (definition is null) return false;
        scheduler.Data.Reminders.Remove(definition);
        return scheduler.SettingsChanged(); // Keep every historical occurrence and acknowledgment.
    });

    public IReadOnlyList<ReminderOccurrence>? AddTestGroup(string id, DateTimeOffset now)
    {
        IReadOnlyList<ReminderOccurrence>? result = null;
        Execute(() =>
        {
            if (!scheduler.Data.Reminders.Any(r => r.Id == id)) return false;
            var group = scheduler.AddTestGroup(id, now);
            if (group.Count == 0) return false;
            result = ReminderDataCodec.Copy(group.ToArray());
            return true;
        });
        return result;
    }

    public IReadOnlyList<ReminderOccurrence>? TakeNextGroup(DateTimeOffset now)
    {
        IReadOnlyList<ReminderOccurrence>? result = null;
        Execute(() =>
        {
            var group = scheduler.TakeNextGroup(now);
            if (group is not null)
            { presentedIds = group.Select(o => o.Id).ToArray(); result = ReminderDataCodec.Copy(group.ToArray()); }
            return scheduler.PersistenceError is null;
        });
        return result;
    }

    public IReadOnlyList<ReminderOccurrence> PresentationGroup(ReminderOccurrence seed, DateTimeOffset now)
    { lock (gate) return Find(seed.Id) is { } actual ? ReminderDataCodec.Copy(scheduler.PresentationGroup(actual, now).ToArray()) : []; }

    public ReminderCommandResult MarkPresented(IReadOnlyList<ReminderOccurrence> group, DateTimeOffset now) => Execute(() =>
    {
        var actual = group.Select(o => Find(o.Id)).OfType<ReminderOccurrence>().ToArray();
        if (actual.Length == 0 || actual.Length != group.Count || !actual.All(o => scheduler.MayNotify(o, now))) return false;
        if (!scheduler.MarkPresented(actual, now)) return false;
        presentedIds = actual.Select(o => o.Id).ToArray();
        return true;
    });

    public IReadOnlyList<ReminderOccurrence> GetPresentation()
    { lock (gate) return ReminderDataCodec.Copy(presentedIds.Select(Find).OfType<ReminderOccurrence>().ToArray()); }

    // Closing is a presentation-only command: deliberately no acknowledgment or disk write.
    public void ClosePresentation(IReadOnlyList<string>? expectedIds = null)
    {
        lock (gate)
            if (expectedIds is null || presentedIds.SequenceEqual(expectedIds)) presentedIds = [];
    }
}

