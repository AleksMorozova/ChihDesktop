using System.Text.Json;
using ChihDesktop.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChihDesktop.Tests;

public sealed class TaskCompanionTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    [Fact]
    public void Due_date_and_reminder_are_independent_and_completion_disarms_reopen()
    {
        var now = DateTimeOffset.Parse("2026-11-01T12:00:00Z");
        var due = PlannerTask.Create(new("Synthetic", DueDate: new(2026, 11, 1)), now);
        Assert.Null(due.RemindAtUtc); Assert.False(due.ReminderDue(now));
        var reminder = PlannerTask.Create(new("Synthetic", RemindAtUtc: now), now);
        Assert.Null(reminder.DueDate); Assert.True(reminder.ReminderDue(now));
        reminder.Complete(now); reminder.Reopen(now);
        Assert.False(reminder.ReminderDue(now));
    }

    [Theory]
    [InlineData("2026-03-29T03:30")]
    [InlineData("2026-10-25T03:30")]
    public void Kyiv_gap_and_overlap_are_rejected(string local) => Assert.Throws<ArgumentException>(() => TaskReminderTime.Resolve(local, "Europe/Kyiv"));

    [Theory]
    [InlineData("2026-01-10T12:00", "2026-01-10T10:00:00Z")]
    [InlineData("2026-07-10T12:00", "2026-07-10T09:00:00Z")]
    public void Kyiv_local_input_resolves_explicitly(string local, string utc) => Assert.Equal(DateTimeOffset.Parse(utc), TaskReminderTime.Resolve(local, "Europe/Kyiv"));

    [Fact]
    public void Policy_respects_quiet_disabled_privacy_and_simultaneous_order()
    {
        var settings = new UserSettings(); var now = DateTimeOffset.UtcNow;
        Assert.False(settings.TaskSummarySpeechEnabled);
        Assert.False(TaskCompanionPolicy.MayDeliver(settings, true, false));
        Assert.False(TaskCompanionPolicy.MayDeliver(settings, false, true));
        Assert.False(TaskCompanionPolicy.MayDeliver(settings with { ReminderNotificationsEnabled = false }, false, false));
        Assert.False(TaskCompanionPolicy.MayDeliver(settings with { TaskReminderNotificationsEnabled = false }, false, false));
        var first = PlannerTask.Create(new("Synthetic private title", RemindAtUtc: now.AddMinutes(-2)), now).ToDto();
        var second = PlannerTask.Create(new("Synthetic second", RemindAtUtc: now), now).ToDto();
        Assert.Equal(first.Id, TaskCompanionPolicy.Next([second, first], now)!.Id);
        Assert.DoesNotContain(first.Title, TaskCompanionPolicy.BubbleTitle(first, settings with { ShowTaskTitlesInBubbles = false }));
        Assert.Null(TaskCompanionPolicy.Next([first with { Status = PlannerTaskStatus.Completed }], now));
    }

    [Fact]
    public async Task Delivery_receipt_survives_restart_snooze_dismiss_and_stale_edit()
    {
        using var db = new Database();
        var task = (await db.Planner.CreateTaskAsync(new("Synthetic", RemindAtUtc: db.Clock.Now), Token)).Value!;
        var delivered = await db.Planner.ChangeReminderAsync(task.Id, "deliver", task.Revision, Token);
        Assert.True(delivered.Success); Assert.Equal(TaskReminderStatus.Delivered, delivered.Value!.ReminderStatus);
        Assert.Equal(PlannerError.Conflict, (await db.Planner.ChangeReminderAsync(task.Id, "snooze", task.Revision, Token)).Error);
        using var restarted = new PlannerDatabaseExecutor(db.Factory, db.Options, NullLogger<PlannerDatabaseExecutor>.Instance);
        var service = new PlannerService(new SqlitePlannerTaskStore(restarted), db.Clock);
        Assert.Null(TaskCompanionPolicy.Next((await service.GetTasksAsync(cancellationToken: Token)).Value!, db.Clock.Now));
        var snoozed = (await service.ChangeReminderAsync(task.Id, "snooze", delivered.Value.Revision, Token)).Value!;
        Assert.Equal(db.Clock.Now.AddMinutes(10), snoozed.SnoozedUntilUtc);
        Assert.Null(TaskCompanionPolicy.Next([snoozed], db.Clock.Now));
        Assert.NotNull(TaskCompanionPolicy.Next([snoozed], db.Clock.Now.AddMinutes(10)));
        var dismissed = (await service.ChangeReminderAsync(task.Id, "dismiss", snoozed.Revision, Token)).Value!;
        Assert.Equal(PlannerTaskStatus.Active, dismissed.Status);
        Assert.Null(TaskCompanionPolicy.Next([dismissed], db.Clock.Now.AddDays(1)));
        Assert.True((await service.DeleteTaskAsync(task.Id, dismissed.Revision, Token)).Success);
        Assert.Empty((await service.GetTasksAsync(cancellationToken: Token)).Value!);
    }

    [Fact]
    public async Task Additive_migration_preserves_original_rows_and_disables_reminders()
    {
        using var db = new Database();
        Guid id = Guid.NewGuid();
        using (var old = db.Factory.CreateDbContext())
        {
            await old.GetService<IMigrator>().MigrateAsync("20261031154318_InitialPlanner", Token);
            await old.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO PlannerTasks (Id, Title, Status, Priority, Category, CreatedAtUtcTicks, UpdatedAtUtcTicks, Revision) VALUES ({id}, {"Synthetic legacy"}, 0, 1, 0, {db.Clock.Now.UtcTicks}, {db.Clock.Now.UtcTicks}, 7)", Token);
        }
        var result = await db.Planner.GetTaskByIdAsync(id, Token);
        Assert.True(result.Success); Assert.Equal(7, result.Value!.Revision); Assert.Equal("Synthetic legacy", result.Value.Title);
        Assert.Null(result.Value.RemindAtUtc); Assert.Equal(TaskReminderStatus.Disabled, result.Value.ReminderStatus);
    }

    [Fact]
    public async Task Bridge_validates_task_reminder_actions_and_explicit_time_offsets()
    {
        using var db = new Database(); using var bridge = new AdminBridge(db.Planner);
        async Task<AdminResponse> Send(string operation, object payload) => await bridge.HandleAsync(JsonSerializer.Serialize(new { protocolVersion = 1, requestId = Guid.NewGuid(), operation, payload }), Token);
        Assert.True((await Send("planner.resolveTime", new { local = "2026-07-10T12:00", timeZoneId = "Europe/Kyiv" })).Success);
        Assert.False((await Send("planner.resolveTime", new { local = "2026-03-29T03:30", timeZoneId = "Europe/Kyiv" })).Success);
        Assert.False((await Send("planner.create", new { task = new { title = "Synthetic", remindAtUtc = "2026-11-01T12:00:00" } })).Success);
        Assert.False((await Send("planner.reminder", new { id = Guid.NewGuid(), expectedRevision = 1, action = "deliver" })).Success);
        var created = (await db.Planner.CreateTaskAsync(new("Synthetic", RemindAtUtc: db.Clock.Now), Token)).Value!;
        Assert.True((await Send("planner.reminder", new { id = created.Id, expectedRevision = created.Revision, action = "snooze" })).Success);
    }

    [Fact]
    public async Task Notification_receipt_failure_does_not_claim_delivery_or_acknowledgment()
    {
        using var db = new Database();
        var task = (await db.Planner.CreateTaskAsync(new("Synthetic", RemindAtUtc: db.Clock.Now), Token)).Value!;
        using (var context = db.Factory.CreateDbContext())
            await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_task_update BEFORE UPDATE ON PlannerTasks BEGIN SELECT RAISE(ABORT, 'Synthetic failure'); END", Token);
        Assert.False((await db.Planner.ChangeReminderAsync(task.Id, "deliver", task.Revision, Token)).Success);
        int reactions = 0; db.Planner.TaskCompleted += () => reactions++;
        Assert.False((await db.Planner.CompleteTaskAsync(task.Id, Token)).Success); Assert.Equal(0, reactions);
        var stored = (await db.Planner.GetTaskByIdAsync(task.Id, Token)).Value!;
        Assert.Equal(TaskReminderStatus.Pending, stored.ReminderStatus); Assert.Equal(task.Revision, stored.Revision);
        Assert.Equal(PlannerTaskStatus.Active, stored.Status);
    }

    [Fact]
    public async Task Completion_event_occurs_only_after_success_once()
    {
        using var db = new Database(); int reactions = 0; db.Planner.TaskCompleted += () => reactions++;
        var task = (await db.Planner.CreateTaskAsync(new("Synthetic"), Token)).Value!;
        Assert.True((await db.Planner.CompleteTaskAsync(task.Id, Token)).Success);
        Assert.True((await db.Planner.CompleteTaskAsync(task.Id, Token)).Success);
        Assert.Equal(1, reactions);
    }

    [Fact]
    public async Task Shared_bubble_close_does_not_complete_and_does_not_repeat_after_restart()
    {
        using var db = new Database(); using var files = new TempFiles();
        var preferences = new UserSettingsService(new UserSettingsStore(files.File("settings.json")), new Autostart());
        var reminders = new ReminderCommands(new ReminderStore(files.File("reminders.json")), db.Clock);
        Assert.True(reminders.UpdateSettings([], false, new(22, 0), new(8, 0)).Success);
        var task = (await db.Planner.CreateTaskAsync(new("Synthetic", RemindAtUtc: db.Clock.Now), Token)).Value!;
        var bubble = new Presenter();
        using (var delivery = new TaskCompanionDelivery(db.Planner, preferences, reminders, db.Clock, null!, NullLogger.Instance,
            new(() => new(), db.Clock.Now), () => { }))
        {
            await delivery.PollAsync(bubble, () => false);
            Assert.True(bubble.IsVisible); Assert.True(delivery.Active);
            delivery.Close(); bubble.HideReminder();
        }
        Assert.Equal(PlannerTaskStatus.Active, (await db.Planner.GetTaskByIdAsync(task.Id, Token)).Value!.Status);
        using var restart = new TaskCompanionDelivery(db.Planner, preferences, reminders, db.Clock, null!, NullLogger.Instance,
            new(() => new(), db.Clock.Now), () => { });
        await restart.PollAsync(bubble, () => false); Assert.False(bubble.IsVisible);
        Assert.Empty(reminders.GetSnapshot().Occurrences); Assert.Empty(reminders.GetSnapshot().Reminders);
    }

    [Fact]
    public async Task Simultaneous_delivery_is_serialized_and_disabled_notifications_stay_pending()
    {
        using var db = new Database(); using var files = new TempFiles();
        var preferences = new UserSettingsService(new UserSettingsStore(files.File("settings.json")), new Autostart());
        var reminders = new ReminderCommands(new ReminderStore(files.File("reminders.json")), db.Clock);
        Assert.True(reminders.UpdateSettings([], false, new(22, 0), new(8, 0)).Success);
        await db.Planner.CreateTaskAsync(new("Synthetic A", RemindAtUtc: db.Clock.Now), Token);
        await db.Planner.CreateTaskAsync(new("Synthetic B", RemindAtUtc: db.Clock.Now), Token);
        Assert.True(preferences.Save(preferences.GetSnapshot() with { TaskReminderNotificationsEnabled = false }).Success);
        var bubble = new Presenter();
        using var delivery = new TaskCompanionDelivery(db.Planner, preferences, reminders, db.Clock, null!, NullLogger.Instance,
            new(() => new(), db.Clock.Now), () => { });
        await delivery.PollAsync(bubble, () => false); Assert.False(bubble.IsVisible);
        Assert.True(preferences.Save(preferences.GetSnapshot() with { TaskReminderNotificationsEnabled = true, ShowTaskTitlesInBubbles = false }).Success);
        await Task.WhenAll(delivery.PollAsync(bubble, () => false), delivery.PollAsync(bubble, () => false));
        Assert.Single(bubble.Occurrences); Assert.DoesNotContain("Synthetic", bubble.Occurrences[0].Title);
        var tasks = (await db.Planner.GetTasksAsync(cancellationToken: Token)).Value!;
        Assert.Single(tasks, t => t.ReminderStatus == TaskReminderStatus.Delivered);
        Assert.Single(tasks, t => t.ReminderStatus == TaskReminderStatus.Pending);
        await delivery.ActAsync(bubble, "snooze"); Assert.False(bubble.IsVisible);
        Assert.All((await db.Planner.GetTasksAsync(cancellationToken: Token)).Value!, t => Assert.Equal(PlannerTaskStatus.Active, t.Status));
    }

    [Theory]
    [InlineData(DialogueCategory.TaskCompletion)]
    [InlineData(DialogueCategory.TaskReminder)]
    [InlineData(DialogueCategory.TaskSummary)]
    public void Task_dialogue_uses_templates_and_exhausts_before_repeat(DialogueCategory category)
    {
        var personality = new PersonalityService(() => new(), DateTimeOffset.UtcNow, new Random(3), () => "Synthetic owner");
        var phrases = Enumerable.Range(0, DialogueCatalog.Phrases[category].Length).Select(_ => personality.NextPhrase(category)).ToArray();
        Assert.Equal(phrases.Length, phrases.Distinct().Count()); Assert.DoesNotContain(phrases, p => p.Contains("{ownerName}"));
    }

    [Fact]
    public async Task Changing_timezone_does_not_reinterpret_stored_instant_or_snooze()
    {
        using var db = new Database();
        var instant = TaskReminderTime.Resolve("2026-12-01T12:00", "Europe/Kyiv");
        var task = (await db.Planner.CreateTaskAsync(new("Synthetic", RemindAtUtc: instant), Token)).Value!;
        var snoozed = (await db.Planner.ChangeReminderAsync(task.Id, "snooze", task.Revision, Token)).Value!;
        var otherZone = new PlannerService(new SqlitePlannerTaskStore(db.Executor), db.Clock, () => "UTC");
        var read = (await otherZone.GetTaskByIdAsync(task.Id, Token)).Value!;
        Assert.Equal(instant, read.RemindAtUtc); Assert.Equal(snoozed.SnoozedUntilUtc, read.SnoozedUntilUtc);
        var disabled = (await otherZone.UpdateTaskAsync(task.Id, new("Synthetic"), read.Revision, Token)).Value!;
        Assert.Equal(TaskReminderStatus.Disabled, disabled.ReminderStatus); Assert.Null(disabled.RemindAtUtc);
    }

    [Fact]
    public void Task_comments_share_existing_personality_cooldown()
    {
        var now = DateTimeOffset.Parse("2026-11-01T12:00:00Z");
        var service = new PersonalityService(() => new(), now);
        Assert.NotNull(service.TaskComment(now, DialogueCategory.TaskCompletion));
        Assert.Null(service.TaskComment(now.AddSeconds(1), DialogueCategory.TaskSummary));
        service.RecordSpeech(now.AddMinutes(20));
        Assert.Null(service.TaskComment(now.AddMinutes(21), DialogueCategory.TaskCompletion));
    }

    [Fact]
    public void Legacy_settings_load_without_rewrite_and_new_preferences_persist()
    {
        using var files = new TempFiles(); var path = files.File("settings.json");
        const string legacy = """{"SchemaVersion":1,"TimeZoneId":"Europe/Kyiv","WalkingSpeed":123,"OnboardingCompleted":true}""";
        File.WriteAllText(path, legacy);
        var settings = new UserSettingsService(new UserSettingsStore(path), new Autostart());
        Assert.Equal(2, settings.GetSnapshot().SchemaVersion);
        Assert.Equal(legacy, File.ReadAllText(path)); Assert.Equal(123, settings.GetSnapshot().WalkingSpeed);
        Assert.True(settings.GetSnapshot().TaskReminderNotificationsEnabled); Assert.False(settings.GetSnapshot().TaskSummarySpeechEnabled);
        Assert.True(settings.Save(settings.GetSnapshot() with { ShowTaskTitlesInBubbles = false, TaskCompletionReactionsEnabled = false }).Success);
        Assert.Equal(legacy, File.ReadAllText(path + ".bak"));
        var restarted = new UserSettingsService(new UserSettingsStore(path), new Autostart()).GetSnapshot();
        Assert.False(restarted.ShowTaskTitlesInBubbles); Assert.False(restarted.TaskCompletionReactionsEnabled);
        Assert.Equal("Europe/Kyiv", restarted.TimeZoneId); Assert.Equal(123, restarted.WalkingSpeed);
    }

    [Fact]
    public async Task Ineligible_reminder_commands_do_not_claim_success()
    {
        using var db = new Database();
        var task = (await db.Planner.CreateTaskAsync(new("Synthetic"), Token)).Value!;
        Assert.Equal(PlannerError.InvalidInput, (await db.Planner.ChangeReminderAsync(task.Id, "snooze", task.Revision, Token)).Error);
        var future = (await db.Planner.CreateTaskAsync(new("Synthetic future", RemindAtUtc: db.Clock.Now.AddDays(1)), Token)).Value!;
        Assert.Equal(PlannerError.InvalidInput, (await db.Planner.ChangeReminderAsync(future.Id, "deliver", future.Revision, Token)).Error);
    }

    [Fact]
    public async Task Closing_task_presentation_during_receipt_does_not_hide_a_new_recurring_bubble()
    {
        using var db = new Database(); using var files = new TempFiles();
        var preferences = new UserSettingsService(new UserSettingsStore(files.File("settings.json")), new Autostart());
        var reminders = new ReminderCommands(new ReminderStore(files.File("reminders.json")), db.Clock);
        Assert.True(reminders.UpdateSettings([], false, new(22, 0), new(8, 0)).Success);
        await db.Planner.CreateTaskAsync(new("Synthetic", RemindAtUtc: db.Clock.Now), Token);
        var deferred = new DeferredStore(new SqlitePlannerTaskStore(db.Executor));
        var service = new PlannerService(deferred, db.Clock); var bubble = new Presenter();
        using var delivery = new TaskCompanionDelivery(service, preferences, reminders, db.Clock, null!, NullLogger.Instance,
            new(() => new(), db.Clock.Now), () => { });
        var pending = delivery.PollAsync(bubble, () => false);
        await deferred.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        delivery.Close(); bubble.HideReminder();
        bubble.Present([new() { Title = "Synthetic recurring", ReminderId = "synthetic-recurring" }], "Synthetic");
        deferred.Continue.SetResult(); await pending;
        Assert.True(bubble.IsVisible); Assert.Equal("Synthetic recurring", bubble.Occurrences.Single().Title); Assert.False(delivery.Active);
    }

    private sealed class DeferredStore(IPlannerTaskStore inner) : IPlannerTaskStore
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<PlannerResult<PlannerTask>> CreateAsync(PlannerTask task, CancellationToken token) => inner.CreateAsync(task, token);
        public async Task<PlannerResult<PlannerTask>> ChangeAsync(Guid id, Func<PlannerTask, bool> change, long? revision, CancellationToken token)
        { Entered.SetResult(); await Continue.Task.WaitAsync(token); return await inner.ChangeAsync(id, change, revision, token); }
        public Task<PlannerResult<bool>> DeleteAsync(Guid id, long? revision, CancellationToken token) => inner.DeleteAsync(id, revision, token);
        public Task<PlannerResult<PlannerTask>> FindAsync(Guid id, CancellationToken token) => inner.FindAsync(id, token);
        public Task<PlannerResult<IReadOnlyList<PlannerTask>>> QueryAsync(PlannerQuery query, DateOnly today, CancellationToken token) => inner.QueryAsync(query, today, token);
    }

    private sealed class Autostart : IAutostartRegistration
    { public string? Read() => null; public void Write(string? command) { } public string Command(bool petOnly) => "synthetic"; }
    private sealed class Presenter : IReminderPresenter
    {
        public event Action<ReminderOccurrence, string>? ActionRequested { add { } remove { } }
        public event Action? DialogueDismissRequested { add { } remove { } }
        public IReadOnlyList<ReminderOccurrence> Occurrences { get; private set; } = [];
        public bool IsVisible { get; private set; }
        public bool IsDialogue => false;
        public bool IsStoreNotice => false;
        public void Present(IReadOnlyList<ReminderOccurrence> occurrences, string text) { Occurrences = occurrences; IsVisible = true; }
        public void PresentDialogue(string mood, string text) { IsVisible = true; }
        public void PresentStoreNotice(string text) { IsVisible = true; }
        public void ShowPersistenceError(string? error) { }
        public void RefreshItems(IReadOnlyList<ReminderOccurrence>? latest = null) { }
        public void HideReminder() { IsVisible = false; Occurrences = []; }
        public void Dispose() { }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.Parse("2026-11-01T12:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Factory(PlannerDatabaseOptions options) : IDbContextFactory<PlannerDbContext>
    {
        public PlannerDbContext CreateDbContext() => new(new DbContextOptionsBuilder<PlannerDbContext>().UseSqlite(options.ConnectionString).Options);
    }
    private sealed class Database : IDisposable
    {
        private readonly TempFiles files = new();
        public PlannerDatabaseOptions Options { get; }
        public Factory Factory { get; }
        public Clock Clock { get; } = new();
        public PlannerDatabaseExecutor Executor { get; }
        public PlannerService Planner { get; }
        public Database()
        {
            Options = new(files.File("synthetic.db")); Factory = new(Options);
            Executor = new(Factory, Options, NullLogger<PlannerDatabaseExecutor>.Instance);
            Planner = new(new SqlitePlannerTaskStore(Executor), Clock);
        }
        public void Dispose() { Executor.Dispose(); files.Dispose(); }
    }
}
