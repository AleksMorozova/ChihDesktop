using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ChihDesktop.Tests;

public sealed class R1ReminderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Definition_edits_reject_silent_retiming_or_recurrence_kind_changes()
    {
        using var files = new TempFiles(); var commands = new ReminderCommands(new ReminderStore(files.File("reminders.json")), userSettings: Settings("UTC"));
        Assert.True(commands.CreateReminder(new() { Title = "Synthetic bound zone", Enabled = false }).Success);
        byte[] original = File.ReadAllBytes(commands.ConfigurationPath);
        var edits = commands.GetSnapshot().Reminders; edits[0].TimeZoneId = "Tokyo Standard Time";
        Assert.False(commands.UpdateSettings(edits, true, new(22, 0), new(8, 0)).Success);
        Assert.Equal(original, File.ReadAllBytes(commands.ConfigurationPath));
        edits = commands.GetSnapshot().Reminders; edits[0].Recurrence = ReminderRecurrence.Weekly;
        Assert.False(commands.UpdateSettings(edits, true, new(22, 0), new(8, 0)).Success);
        Assert.Equal(original, File.ReadAllBytes(commands.ConfigurationPath));
    }

    [Fact]
    public void Recovering_legacy_backup_preserves_original_before_upgrading_for_a_new_definition()
    {
        using var files = new TempFiles(); string path = files.File("reminders.json");
        string original = JsonSerializer.Serialize(SyntheticReminderFixtures.Schedule(), ReminderStore.JsonOptions);
        File.WriteAllText(path, "damaged primary"); File.WriteAllText(path + ".bak", original);
        var commands = new ReminderCommands(new ReminderStore(path));
        Assert.False(File.Exists(path + ".pre-v03-backup"));
        Assert.True(commands.CreateReminder(new() { Title = "Synthetic new definition", Enabled = false }).Success);
        Assert.Equal(original, File.ReadAllText(path + ".pre-v03-backup"));
        Assert.Equal(original, File.ReadAllText(path + ".bak"));
    }

    [Fact]
    public void Damaged_permanent_reminder_backup_blocks_writes_without_changing_the_primary()
    {
        using var files = new TempFiles(); string path = files.File("reminders.json");
        string original = JsonSerializer.Serialize(SyntheticReminderFixtures.Schedule(), ReminderStore.JsonOptions);
        File.WriteAllText(path, original); File.WriteAllText(path + ".pre-v03-backup", "damaged snapshot");
        var commands = new ReminderCommands(new ReminderStore(path));
        Assert.False(commands.DeleteReminder("sample-a").Success);
        Assert.Equal(original, File.ReadAllText(path)); Assert.Equal(7, commands.GetSnapshot().Reminders.Count);
    }

    [Fact]
    public void Reminder_create_edit_disable_delete_keeps_history_and_requires_explicit_acknowledgment()
    {
        using var files = new TempFiles();
        var settings = Settings("UTC");
        var commands = new ReminderCommands(new ReminderStore(files.File("reminders.json")), userSettings: settings);
        var definition = new ReminderDefinition { Id = "synthetic-reminder", Title = "  Synthetic reminder  ", Time = new(9, 0), Enabled = false };
        Assert.True(commands.CreateReminder(definition).Success);
        commands.Tick(Now, TimeSpan.Zero); Assert.Empty(commands.GetSnapshot().Occurrences);
        var edits = commands.GetSnapshot().Reminders; edits[0].Enabled = true; edits[0].Title = "Edited reminder";
        Assert.True(commands.UpdateSettings(edits, false, new(22, 0), new(8, 0)).Success);
        commands.Tick(Now, TimeSpan.Zero);
        var occurrence = Assert.Single(commands.GetSnapshot().Occurrences);
        Assert.Equal("Edited reminder", occurrence.Title); Assert.Equal("UTC", occurrence.TimeZoneId);
        Assert.True(commands.Snooze(occurrence.Id, Now).Success);
        commands.ClosePresentation(); Assert.Equal(ReminderAcknowledgment.Pending, commands.GetSnapshot().Occurrences.Single().Acknowledgment);
        Assert.True(commands.Acknowledge(occurrence.Id, false, Now).Success);
        Assert.Equal(ReminderAcknowledgment.Dismissed, commands.GetSnapshot().Occurrences.Single().Acknowledgment);
        Assert.True(commands.DeleteReminder(definition.Id).Success);
        Assert.Empty(commands.GetSnapshot().Reminders); Assert.Single(commands.GetSnapshot().Occurrences);
        Assert.False(commands.MayNotify(occurrence, Now.AddMinutes(20)));
        Assert.False(commands.CreateReminder(definition).Success); // Historical identity cannot be recycled.
        Assert.True(commands.Acknowledge(occurrence.Id, true, Now).Success);
        Assert.Equal(ReminderAcknowledgment.Completed, commands.GetSnapshot().Occurrences.Single().Acknowledgment);
    }

    [Fact]
    public void Failed_reminder_create_delete_edit_roll_back_all_state()
    {
        using var files = new TempFiles(); var store = new FailingReminderStore(files.File("reminders.json"));
        var commands = new ReminderCommands(store);
        Assert.True(commands.CreateReminder(new() { Id = "synthetic", Title = "Original", Enabled = false }).Success);
        string original = JsonSerializer.Serialize(commands.GetSnapshot()); store.Fail = true;
        Assert.False(commands.CreateReminder(new() { Title = "Unsaved", Enabled = true }).Success);
        Assert.Equal(original, JsonSerializer.Serialize(commands.GetSnapshot()));
        Assert.False(commands.DeleteReminder("synthetic").Success);
        Assert.Equal(original, JsonSerializer.Serialize(commands.GetSnapshot()));
        var edits = commands.GetSnapshot().Reminders; edits[0].Title = "Unsaved edit";
        Assert.False(commands.UpdateSettings(edits, false, new(22, 0), new(8, 0)).Success);
        Assert.Equal(original, JsonSerializer.Serialize(commands.GetSnapshot()));
    }

    [Theory]
    [InlineData("2026-03-29T00:59:00Z", "2026-03-29T01:00:00Z")]
    [InlineData("2026-10-25T00:30:00Z", "2026-10-25T01:30:00Z")]
    public void Kyiv_legacy_dst_and_preference_change_keep_occurrence_identity_and_utc_snoozes(string first, string second)
    {
        using var files = new TempFiles(); string path = files.File("reminders.json");
        var legacy = new ReminderData { Version = 2, TimeZoneId = "Europe/Kyiv", QuietHoursEnabled = false,
            Reminders = [new() { Id = "synthetic-dst", Title = "Synthetic DST", Time = new(3, 30) }] };
        File.WriteAllText(path, JsonSerializer.Serialize(legacy, ReminderStore.JsonOptions));
        var settings = Settings("UTC"); var commands = new ReminderCommands(new ReminderStore(path), userSettings: settings);
        var a = DateTimeOffset.Parse(first); var b = DateTimeOffset.Parse(second);
        commands.Tick(a, TimeSpan.Zero); commands.Tick(b, TimeSpan.Zero);
        var occurrence = Assert.Single(commands.GetSnapshot().Occurrences);
        Assert.True(commands.Snooze(occurrence.Id, b).Success);
        var before = JsonSerializer.Serialize(commands.GetSnapshot());
        Assert.True(settings.Save(settings.GetSnapshot() with { TimeZoneId = "Tokyo Standard Time" }).Success);
        Assert.Equal(before, JsonSerializer.Serialize(commands.GetSnapshot()));
        Assert.Equal(b.AddMinutes(10), commands.GetSnapshot().Occurrences.Single().SnoozedUntilUtc);
        commands.Tick(b, TimeSpan.Zero); Assert.Single(commands.GetSnapshot().Occurrences);
        var restarted = new ReminderCommands(new ReminderStore(path), userSettings: settings);
        restarted.Tick(b, TimeSpan.Zero); Assert.Single(restarted.GetSnapshot().Occurrences);
        Assert.Equal("Europe/Kyiv", restarted.GetSnapshot().TimeZoneId);
        Assert.Equal("Europe/Kyiv", restarted.GetSnapshot().Occurrences.Single().TimeZoneId);
    }

    [Fact]
    public void Changing_preferred_zone_only_affects_new_definitions_and_planner_today()
    {
        using var files = new TempFiles(); var settings = Settings("UTC");
        var commands = new ReminderCommands(new ReminderStore(files.File("reminders.json")), userSettings: settings);
        Assert.True(commands.CreateReminder(new() { Id = "first", Title = "Synthetic UTC", Time = new(9, 0), Enabled = true }).Success);
        Assert.True(settings.Save(settings.GetSnapshot() with { TimeZoneId = "Tokyo Standard Time" }).Success);
        Assert.True(commands.CreateReminder(new() { Id = "second", Title = "Synthetic Tokyo", Time = new(9, 0), Enabled = true }).Success);
        var data = commands.GetSnapshot();
        Assert.Equal("UTC", data.Reminders[0].TimeZoneId); Assert.Equal("Tokyo Standard Time", data.Reminders[1].TimeZoneId);
        commands.Tick(Now, TimeSpan.Zero);
        var dates = commands.GetSnapshot().Occurrences;
        Assert.Equal(2, dates.Count);
        Assert.Equal(9, dates.Single(o => o.ReminderId == "first").DueAtUtc.Hour);
        Assert.Equal(0, dates.Single(o => o.ReminderId == "second").DueAtUtc.Hour);
    }

    [Fact]
    public async Task Preferred_zone_changes_planner_queries_without_migrating_sqlite_tasks()
    {
        using var files = new TempFiles(); var clock = new FixedClock(); var settings = Settings("UTC");
        using var host = DesktopComposition.CreateHost(clock: clock, userDataDirectory: files.Directory, autostart: new MemoryAutostart(), userSettings: settings);
        var planner = host.Services.GetRequiredService<IPlannerService>();
        var created = await planner.CreateTaskAsync(new("Synthetic date", DueDate: new(2026, 11, 1)), TestContext.Current.CancellationToken);
        Assert.True(created.Success);
        Assert.Empty((await planner.GetTasksAsync(new(PlannerView.Today), TestContext.Current.CancellationToken)).Value!);
        Assert.True(settings.Save(settings.GetSnapshot() with { TimeZoneId = "Tokyo Standard Time" }).Success);
        Assert.Single((await planner.GetTasksAsync(new(PlannerView.Today), TestContext.Current.CancellationToken)).Value!);
        Assert.Equal(created.Value, (await planner.GetTaskByIdAsync(created.Value!.Id, TestContext.Current.CancellationToken)).Value);
    }

    [Fact]
    public void Muting_notifications_retains_due_records_and_reenabling_does_not_duplicate_them()
    {
        using var files = new TempFiles(); var settings = Settings("UTC");
        var commands = new ReminderCommands(new ReminderStore(files.File("reminders.json")), userSettings: settings);
        Assert.True(commands.CreateReminder(new() { Title = "Synthetic", Time = new(9, 0) }).Success);
        Assert.True(settings.Save(settings.GetSnapshot() with { ReminderNotificationsEnabled = false }).Success);
        commands.Tick(Now, TimeSpan.Zero);
        Assert.Single(commands.GetSnapshot().Occurrences); Assert.Null(commands.TakeNextGroup(Now));
        Assert.True(settings.Save(settings.GetSnapshot() with { ReminderNotificationsEnabled = true }).Success);
        Assert.NotNull(commands.TakeNextGroup(Now)); commands.Tick(Now, TimeSpan.Zero);
        Assert.Single(commands.GetSnapshot().Occurrences);
    }

    [Fact]
    public async Task Settings_entry_is_allowlisted_and_rejects_extra_payload_fields()
    {
        using var files = new TempFiles(); using var host = DesktopComposition.CreateHost(userDataDirectory: files.Directory, autostart: new MemoryAutostart());
        int opened = 0; using var bridge = new AdminBridge(host.Services.GetRequiredService<IPlannerService>(), () => opened++);
        string Request(object payload) => JsonSerializer.Serialize(new { protocolVersion = 1, requestId = Guid.NewGuid().ToString(), operation = "settings.open", payload });
        Assert.True((await bridge.HandleAsync(Request(new { }), TestContext.Current.CancellationToken)).Success);
        Assert.Equal(1, opened);
        Assert.Equal("InvalidPayload", (await bridge.HandleAsync(Request(new { file = "not-allowed" }), TestContext.Current.CancellationToken)).Error!.Code);
        Assert.Equal(1, opened); Assert.Empty(Directory.GetFiles(files.Directory));
    }

    private static UserSettingsService Settings(string zone) => new(new MemorySettings { Data = new() { TimeZoneId = zone } }, new MemoryAutostart());
    private sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 10, 31, 21, 0, 0, TimeSpan.Zero); }
    private sealed class FailingReminderStore(string path) : ReminderStore(path)
    {
        public bool Fail;
        protected override void CommitFile(string temporary, string destination) { if (Fail && destination == FilePath) throw new IOException("Synthetic commit failure"); base.CommitFile(temporary, destination); }
    }
}
