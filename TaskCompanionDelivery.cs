using Microsoft.Extensions.Logging;

namespace ChihDesktop;

// Shares the existing presenter and timer; never writes to ReminderStore.
internal sealed class TaskCompanionDelivery(IPlannerService planner, UserSettingsService settings,
    IReminderCommands reminders, TimeProvider clock, TaskListWindowManager tasks, ILogger logger,
    PersonalityService personality, Action shown, AdminDiagnostics? diagnostics = null) : IDisposable
{
    private readonly AdminDiagnostics log = diagnostics ?? AdminDiagnostics.Disabled;
    private readonly CancellationTokenSource lifetime = new();
    private PlannerTaskDto? current;
    private bool busy, disposed;
    private DateTimeOffset nextPoll;
    private DateTimeOffset nextSpeech;
    private DateTimeOffset nextSummary;
    private int completion;
    private IPlannerActivity? activity;
    public void Start()
    {
        activity = planner as IPlannerActivity;
        if (activity is not null) activity.TaskCompleted += OnCompleted;
        nextSummary = clock.GetUtcNow().AddMinutes(30);
    }
    private void OnCompleted() => Interlocked.Exchange(ref completion, 1);
    public bool Active => current is not null;
    public void Close() => current = null;

    public async Task RefreshActiveAsync(IReminderPresenter bubble)
    {
        if (busy || disposed || current is not { } task) return;
        var preferences = settings.GetSnapshot();
        if (!TaskCompanionPolicy.MayDeliver(preferences, reminders.IsQuiet(clock.GetUtcNow()), false) ||
            !preferences.ShowTaskTitlesInBubbles && bubble.Occurrences.Any(o => o.Title == task.Title))
        { current = null; bubble.HideReminder(); return; }
        busy = true;
        try
        {
            var latest = await planner.GetTaskByIdAsync(task.Id, lifetime.Token);
            if (disposed || current?.Id != task.Id) return;
            if (!latest.Success || latest.Value!.Status != PlannerTaskStatus.Active || latest.Value.Revision != task.Revision)
            { current = null; bubble.HideReminder(); }
        }
        catch (Exception ex) { logger.LogWarning("Task companion refresh failed ({Type}).", ex.GetType().Name); }
        finally { busy = false; }
    }

    public async Task PollAsync(IReminderPresenter bubble, Func<bool> blocked)
    {
        if (busy || disposed || Active || clock.GetUtcNow() < nextPoll) return;
        var now = clock.GetUtcNow();
        if (!settings.CanWrite || !TaskCompanionPolicy.MayDeliver(settings.GetSnapshot(), reminders.IsQuiet(now), blocked())) return;
        busy = true; nextPoll = now.AddSeconds(30);
        try
        {
            var result = await planner.GetTasksAsync(new(PlannerView.AllActive), lifetime.Token);
            if (disposed || bubble.IsVisible || blocked()) return;
            if (!result.Success) { log.Write("task.query.failed", new { code = result.Error.ToString() }); logger.LogWarning("Task companion query failed ({Code}).", result.Error); return; }
            var next = TaskCompanionPolicy.Next(result.Value!, clock.GetUtcNow());
            if (next is null)
            {
                var options = settings.GetSnapshot();
                bool completed = Interlocked.Exchange(ref completion, 0) == 1;
                if (!personality.Enabled || clock.GetUtcNow() < nextSpeech || DesktopActivity.IsFullscreenApp() ||
                    personality.IsQuiet(reminders.LocalNow(clock.GetUtcNow()), reminders.IsQuiet(clock.GetUtcNow()))) return;
                bool react = completed && options.TaskCompletionReactionsEnabled;
                var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), AppTimeZones.Get(options.TimeZoneId)).DateTime);
                if (!react && (!options.TaskSummarySpeechEnabled || !result.Value!.Any(t => TaskCompanionPolicy.InDesktopList(t, today, clock.GetUtcNow(), false)) || clock.GetUtcNow() < nextSummary)) return;
                var phrase = personality.TaskComment(clock.GetUtcNow(), react ? DialogueCategory.TaskCompletion : DialogueCategory.TaskSummary);
                if (phrase is null) return;
                bubble.PresentDialogue(personality.MoodLabel, phrase);
                nextSpeech = clock.GetUtcNow().AddMinutes(Math.Max(30, options.Personality.MinimumCommentIntervalMinutes));
                nextSummary = nextSpeech;
                shown(); return;
            }
            if (planner is not ITaskReminderCommands commands) return;
            // Recheck notification preferences after I/O, before exposing a title.
            var preferences = settings.GetSnapshot();
            if (!TaskCompanionPolicy.MayDeliver(preferences, reminders.IsQuiet(clock.GetUtcNow()), blocked())) return;
            current = next;
            var occurrence = new ReminderOccurrence { Id = next.Id.ToString(), ReminderId = "task-companion",
                Title = TaskCompanionPolicy.BubbleTitle(next, preferences), RequiresConfirmation = true };
            bubble.Present([occurrence], personality.Enabled ? personality.NextPhrase(DialogueCategory.TaskReminder) : "Напоминание о твоей задаче.");
            personality.RecordSpeech(clock.GetUtcNow());
            if (bubble is ReminderBubble native) { native.AddTaskOpenAction(tasks.Show); native.SetTaskActionsEnabled(false); }
            shown();
            var receipt = await commands.ChangeReminderAsync(next.Id, "deliver", next.Revision, lifetime.Token);
            log.Write("task.delivery.receipt", new { receipt.Success, code = receipt.Error.ToString() });
            if (disposed || current?.Id != next.Id) return;
            if (!receipt.Success || receipt.Value!.ReminderStatus != TaskReminderStatus.Delivered)
            {
                current = null; bubble.HideReminder();
                logger.LogWarning("Task companion delivery receipt failed ({Code}).", receipt.Error);
                if (receipt.Error != PlannerError.Conflict)
                { bubble.PresentStoreNotice("Не удалось сохранить доставку напоминания о задаче. Оно остаётся ожидающим. Проверь «Мои задачи» и доступ к базе."); shown(); }
                return; // Remains pending on write failure; next poll can retry.
            }
            if (current?.Id == next.Id) current = receipt.Value;
            if (bubble is ReminderBubble ready) ready.SetTaskActionsEnabled(true);
        }
        catch (Exception ex) { if (!disposed) { log.Write("task.delivery.failed", error: ex); if (current is not null) { current = null; bubble.HideReminder(); } logger.LogWarning("Task companion delivery failed ({Type}).", ex.GetType().Name); } }
        finally { busy = false; }
    }

    public async Task ActAsync(IReminderPresenter bubble, string action)
    {
        if (busy || current is not { } task || planner is not ITaskReminderCommands commands) return;
        busy = true;
        if (bubble is ReminderBubble native) native.SetTaskActionsEnabled(false);
        try
        {
            var result = action == "done" ? await planner.CompleteTaskAsync(task.Id, lifetime.Token)
                : await commands.ChangeReminderAsync(task.Id, action == "snooze" ? "snooze" : "dismiss", task.Revision, lifetime.Token);
            log.Write("task.action.result", new { result.Success, code = result.Error.ToString() });
            if (disposed || current?.Id != task.Id) return;
            if (!result.Success)
            {
                bubble.RefreshItems();
                if (bubble is ReminderBubble failed) failed.AddTaskOpenAction(tasks.Show);
                bubble.ShowPersistenceError(result.Message); return;
            }
            current = null; bubble.HideReminder(); nextPoll = clock.GetUtcNow().AddSeconds(30);
        }
        catch (Exception ex) { if (!disposed && current?.Id == task.Id) { bubble.ShowPersistenceError("Не сохранено. Открой «Мои задачи» и обнови список."); logger.LogWarning("Task companion action failed ({Type}).", ex.GetType().Name); } }
        finally { busy = false; if (!disposed && current?.Id == task.Id && bubble is ReminderBubble ready) ready.SetTaskActionsEnabled(true); }
    }
    public void Dispose() { disposed = true; if (activity is not null) activity.TaskCompleted -= OnCompleted; lifetime.Cancel(); lifetime.Dispose(); current = null; }
}
