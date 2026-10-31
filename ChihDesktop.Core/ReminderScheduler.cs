namespace ChihDesktop;

// Pure scheduling/acknowledgment logic. Legacy dates retain Kyiv rules; new definitions bind their own zone.
// daily occurrence identity is (reminder ID, local date), never an elapsed 24-hour timer.
internal sealed class ReminderScheduler
{
    private readonly IReminderPersistence store;
    private readonly Func<bool> notificationsEnabled;
    private readonly Dictionary<string, TimeSpan> activeIntervals = new();
    private DateTimeOffset nextAutomaticBubbleUtc = DateTimeOffset.MinValue;
    internal ReminderData Data { get; private set; }
    private ReminderData committed;
    private DateTimeOffset committedBubbleUtc = DateTimeOffset.MinValue;
    public TimeZoneInfo Zone => AppTimeZones.Get(Data.TimeZoneId);
    public string? PersistenceError => store.Error;
    public string? StartupWarning { get; }
    public string ConfigurationPath => store.FilePath;
    public event Action? Changed;

    public ReminderScheduler(IReminderPersistence store, DateTimeOffset? migrationNow = null, TimeProvider? timeProvider = null, Func<bool>? notificationsEnabled = null)
    {
        this.store = store;
        this.notificationsEnabled = notificationsEnabled ?? (() => true);
        var load = store.Load();
        Data = load.Data is null ? new ReminderData() : ReminderDataCodec.Copy(load.Data);
        // A blocked store never starts a fresh medication schedule, and public load-result
        // objects must not become an indirect reference to live scheduler state.
        committed = ReminderDataCodec.Copy(Data);
        StartupWarning = load.Message;
    }

    public DateTime LocalNow(DateTimeOffset now) => TimeZoneInfo.ConvertTime(now, Zone).DateTime;
    public DateOnly LocalDate(DateTimeOffset now) => DateOnly.FromDateTime(LocalNow(now));

    public bool IsQuiet(DateTimeOffset now)
    {
        if (!Data.QuietHoursEnabled) return false;
        var time = TimeOnly.FromDateTime(LocalNow(now));
        return Data.QuietStart == Data.QuietEnd || (Data.QuietStart < Data.QuietEnd
            ? time >= Data.QuietStart && time < Data.QuietEnd
            : time >= Data.QuietStart || time < Data.QuietEnd);
    }

    public void Tick(DateTimeOffset now, TimeSpan activeElapsed)
    {
        if (!store.CanWrite) return;

        var intervalsBefore = new Dictionary<string, TimeSpan>(activeIntervals);
        bool changed = false;
        var local = LocalNow(now);
        var date = DateOnly.FromDateTime(local);
        foreach (var reminder in Data.Reminders)
        {
            var reminderZone = AppTimeZones.Get(reminder.TimeZoneId ?? Data.TimeZoneId);
            local = TimeZoneInfo.ConvertTime(now, reminderZone).DateTime;
            date = DateOnly.FromDateTime(local);
            if (!reminder.Enabled) { activeIntervals.Remove(reminder.Id); continue; }
            if (reminder.Recurrence != ReminderRecurrence.RunningInterval)
            {
                if (reminder.Recurrence == ReminderRecurrence.Weekly && local.DayOfWeek != reminder.WeeklyDay) continue;
                // On start/resume catch up today's due items once; never create a multi-day backlog.
                // LastFired also prevents repeats after DST fall-back and manual backward clock changes.
                if (TimeOnly.FromDateTime(local) < reminder.Time || reminder.LastFiredLocalDate >= date ||
                    Data.Occurrences.Any(o => o.ReminderId == reminder.Id && o.LocalDate == date && !o.IsManualTest)) continue;
                CreateOccurrence(reminder, now, date);
                reminder.LastFiredLocalDate = date;
                changed = true;
            }
            else
            {
                // A long timer gap represents sleep/stall: don't replay interval reminders.
                var elapsed = activeIntervals.GetValueOrDefault(reminder.Id);
                if (activeElapsed > TimeSpan.Zero && activeElapsed <= TimeSpan.FromSeconds(30)) elapsed += activeElapsed;
                if (elapsed >= TimeSpan.FromMinutes(reminder.IntervalMinutes))
                {
                    elapsed = TimeSpan.Zero;
                    // Coalesce unresolved interval reminders rather than growing a queue every two minutes.
                    if (!Data.Occurrences.Any(o => o.ReminderId == reminder.Id && !o.IsManualTest &&
                        o.Acknowledgment == ReminderAcknowledgment.Pending))
                    {
                        CreateOccurrence(reminder, now, date);
                        reminder.LastFiredLocalDate = date;
                        changed = true;
                    }
                }
                activeIntervals[reminder.Id] = elapsed;
            }
        }
        if (changed && !Save())
        {
            activeIntervals.Clear();
            foreach (var entry in intervalsBefore) activeIntervals.Add(entry.Key, entry.Value);
        }
    }

    private ReminderOccurrence CreateOccurrence(ReminderDefinition definition, DateTimeOffset now, DateOnly date, bool manual = false)
    {
        var occurrence = new ReminderOccurrence
        {
            ReminderId = definition.Id, Title = definition.Title, LocalDate = date,
            TimeZoneId = definition.TimeZoneId ?? Data.TimeZoneId,
            DueAtUtc = !manual && definition.Recurrence != ReminderRecurrence.RunningInterval
                ? DailyDueUtc(date, definition.Time, AppTimeZones.Get(definition.TimeZoneId ?? Data.TimeZoneId)) : now.ToUniversalTime(), RequiresConfirmation = definition.RequiresConfirmation,
            Message = definition.MessageVariants[Random.Shared.Next(definition.MessageVariants.Count)], IsManualTest = manual,
            BubbleGroup = definition.BubbleGroup
        };
        Data.Occurrences.Add(occurrence);
        return occurrence;
    }

    private static DateTimeOffset DailyDueUtc(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var localDue = date.ToDateTime(time, DateTimeKind.Unspecified);
        // A skipped local time becomes due at the first valid minute after the DST gap.
        while (zone.IsInvalidTime(localDue)) localDue = localDue.AddMinutes(1);
        // During the repeated hour use the earlier occurrence; local-date identity still deduplicates both.
        var offset = zone.IsAmbiguousTime(localDue) ? zone.GetAmbiguousTimeOffsets(localDue).Max() : zone.GetUtcOffset(localDue);
        return new DateTimeOffset(localDue, offset).ToUniversalTime();
    }

    public ReminderOccurrence AddTest(DateTimeOffset now)
    {
        return AddTestGroup("test-break", now)[0];
    }

    public IReadOnlyList<ReminderOccurrence> AddTestGroup(string reminderId, DateTimeOffset now)
    {
        var definition = Data.Reminders.First(r => r.Id == reminderId);
        var definitions = string.IsNullOrEmpty(definition.BubbleGroup) ? [definition] : Data.Reminders.Where(r =>
            r.BubbleGroup == definition.BubbleGroup && r.Time == definition.Time && r.Recurrence == definition.Recurrence &&
            r.WeeklyDay == definition.WeeklyDay).ToArray();
        string batch = Guid.NewGuid().ToString("N");
        var occurrences = definitions.Select(r => CreateOccurrence(r, now, LocalDate(now), true)).ToList();
        foreach (var occurrence in occurrences) occurrence.PreviewBatchId = batch;
        return Save() ? occurrences : [];
    }

    public bool MayNotify(ReminderOccurrence occurrence, DateTimeOffset now)
    {
        if (!occurrence.IsManualTest && !notificationsEnabled()) return false;
        if (occurrence.IsLegacyHistory || occurrence.DueAtUtc > now || occurrence.Acknowledgment != ReminderAcknowledgment.Pending || occurrence.SnoozedUntilUtc > now) return false;
        var definition = Data.Reminders.FirstOrDefault(r => r.Id == occurrence.ReminderId);
        if (definition is null) return false;
        if (!occurrence.IsManualTest && definition?.Enabled != true) return false;
        // A weekly scheduled item is never automatically delivered on another weekday.
        // An explicit, clearly labeled preview is isolated from the real schedule and can run any day/time.
        var local = TimeZoneInfo.ConvertTime(now, AppTimeZones.Get(occurrence.TimeZoneId ?? definition?.TimeZoneId ?? Data.TimeZoneId)).DateTime;
        if (!occurrence.IsManualTest && definition?.Recurrence == ReminderRecurrence.Weekly &&
            DateOnly.FromDateTime(local) != occurrence.LocalDate) return false;
        return occurrence.IsManualTest || !IsQuiet(now) || definition?.BypassQuietHours == true;
    }

    public ReminderOccurrence? TakeNext(DateTimeOffset now)
        => TakeNextGroup(now)?.FirstOrDefault();

    public IReadOnlyList<ReminderOccurrence>? TakeNextGroup(DateTimeOffset now)
    {
        if (now < nextAutomaticBubbleUtc) return null;
        var occurrence = Data.Occurrences.Where(o => o.NotifiedAtUtc is null && MayNotify(o, now))
            .OrderBy(o => Data.Reminders.FirstOrDefault(r => r.Id == o.ReminderId)?.Recurrence == ReminderRecurrence.RunningInterval ? 1 : 0)
            .ThenBy(o => o.SnoozedUntilUtc ?? o.DueAtUtc).FirstOrDefault();
        if (occurrence is null) return null;
        var group = PresentationGroup(occurrence, now);
        return MarkPresented(group, now) ? group : null;
    }

    public IReadOnlyList<ReminderOccurrence> PresentationGroup(ReminderOccurrence occurrence, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(occurrence.BubbleGroup)) return [occurrence];
        return Data.Occurrences.Where(o => o.BubbleGroup == occurrence.BubbleGroup && o.LocalDate == occurrence.LocalDate &&
            o.DueAtUtc == occurrence.DueAtUtc && o.IsManualTest == occurrence.IsManualTest && o.PreviewBatchId == occurrence.PreviewBatchId &&
            (o.Id == occurrence.Id || o.NotifiedAtUtc is null) && MayNotify(o, now)).ToList();
    }

    public bool MarkPresented(ReminderOccurrence occurrence, DateTimeOffset now)
        => MarkPresented([occurrence], now);

    public bool MarkPresented(IReadOnlyList<ReminderOccurrence> occurrences, DateTimeOffset now)
    {
        foreach (var occurrence in occurrences)
        {
            occurrence.NotifiedAtUtc = now.ToUniversalTime();
            occurrence.SnoozedUntilUtc = null;
        }
        nextAutomaticBubbleUtc = now.AddMinutes(2);
        return Save();
    }

    public bool Snooze(ReminderOccurrence occurrence, DateTimeOffset now)
    {
        if (occurrence.Acknowledgment != ReminderAcknowledgment.Pending) return false;
        occurrence.SnoozedUntilUtc = now.ToUniversalTime().AddMinutes(10);
        occurrence.NotifiedAtUtc = null;
        return Save();
    }

    public bool Acknowledge(ReminderOccurrence occurrence, bool completed, DateTimeOffset now)
    {
        occurrence.Acknowledgment = completed ? ReminderAcknowledgment.Completed : ReminderAcknowledgment.Dismissed;
        occurrence.AcknowledgedAtUtc = now.ToUniversalTime();
        occurrence.SnoozedUntilUtc = null;
        return Save();
    }

    public bool SettingsChanged()
    {
        if (!Save()) return false;
        activeIntervals.Clear();
        return true;
    }

    public bool SavePreferences() => Save();

    private bool Save()
    {
        bool success = store.Save(Data);
        if (success) { committed = ReminderDataCodec.Copy(Data); committedBubbleUtc = nextAutomaticBubbleUtc; }
        else { Data = ReminderDataCodec.Copy(committed); nextAutomaticBubbleUtc = committedBubbleUtc; }
        // UI must refresh on failure too, to undo a clicked checkbox and display the error.
        Changed?.Invoke();
        return success;
    }
}

