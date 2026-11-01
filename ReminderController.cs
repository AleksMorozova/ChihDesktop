using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Microsoft.Extensions.Logging;

namespace ChihDesktop;

public sealed class ReminderController : IDisposable
{
    private readonly Window owner;
    private readonly TaskCompanionDelivery? companion;
    private readonly UserSettingsService? companionSettings;
    private readonly IReminderCommands scheduler;
    private readonly IReminderPresenter bubble;
    private readonly TimeProvider clock;
    private readonly ILogger<ReminderController> logger;
    private readonly PersonalityService personality;
    private readonly Func<bool> interactionBlocked;
    private readonly Func<CatState> catState;
    private readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer autoHide = new() { Interval = TimeSpan.FromSeconds(45) };
    private readonly Stopwatch activeClock = Stopwatch.StartNew();
    private TimeSpan lastTick;
    private ReminderCenter? center;
    private bool disposed;
    private string? lastNotifiedError;
    private string? lastManualPetReply;
    private DateTimeOffset lastManualPetAt;

    public bool PersonalityEnabled => personality.Enabled;

    public ReminderController(Window owner, Func<bool> interactionBlocked, Func<CatState> catState,
        IReminderCommands commands, IReminderPresenter presenter, PersonalityService personality,
        TimeProvider clock, ILogger<ReminderController> logger, IPlannerService? planner = null, UserSettingsService? settings = null, TaskListWindowManager? tasks = null, AdminDiagnostics? diagnostics = null)
    {
        this.owner = owner;
        this.interactionBlocked = interactionBlocked;
        this.catState = catState;
        scheduler = commands;
        this.personality = personality;
        this.clock = clock;
        this.logger = logger;
        bubble = presenter;
        companionSettings = settings; if (settings is not null) settings.Changed += OnCompanionSettings;
        if (planner is not null && settings is not null && tasks is not null) { companion = new(planner, settings, commands, clock, tasks, logger, personality, () => { autoHide.Stop(); autoHide.Interval = TimeSpan.FromSeconds(45); autoHide.Start(); }, diagnostics); companion.Start(); }
        bubble.ActionRequested += OnBubbleAction;
        bubble.DialogueDismissRequested += HideBubble;
        autoHide.Tick += (_, _) => HideBubble();
        tick.Tick += (_, _) => Poll();
        scheduler.Changed += OnChanged;
        scheduler.OperationFailed += OnOperationFailed;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        tick.Start();
        Poll();
        if (scheduler.PersistenceError is null && scheduler.StartupWarning is { } warning)
        {
            if (bubble.IsVisible) bubble.ShowPersistenceError(warning);
            else { bubble.PresentStoreNotice(warning); autoHide.Interval = TimeSpan.FromSeconds(45); autoHide.Start(); }
        }
    }

    private void Poll(bool resumed = false)
    {
        if (disposed) return;
        var elapsed = activeClock.Elapsed;
        var delta = resumed ? TimeSpan.Zero : elapsed - lastTick;
        lastTick = elapsed;
        var now = clock.GetUtcNow();
        scheduler.Tick(now, delta);
        personality.Update(now, scheduler.LocalNow(now), delta, DesktopActivity.IdleTime(), catState());
        if (resumed) personality.DeferSpontaneous(now);
        center?.RefreshForDateChange();
        if (companion?.Active != true && bubble.Occurrences.Count > 0 && !bubble.Occurrences.Any(o => scheduler.MayNotify(o, now))) HideBubble();
        bool fullscreen = DesktopActivity.IsFullscreenApp();
        bool quiet = personality.IsQuiet(scheduler.LocalNow(now), scheduler.IsQuiet(now));
        if (companion?.Active == true) _ = companion.RefreshActiveAsync(bubble);
        if (bubble.IsDialogue && (!personality.Enabled || quiet || fullscreen || interactionBlocked())) HideBubble();
        if (interactionBlocked()) return;
        // A scheduled reminder preempts chatter, but never another reminder.
        if (bubble.IsVisible && !bubble.IsDialogue) return;
        var next = scheduler.TakeNextGroup(now);
        if (next is not null) { Present(next); return; }
        if (bubble.IsVisible) return;
        if (companion is not null) _ = companion.PollAsync(bubble, () => disposed || interactionBlocked() || scheduler.HasWaitingNotification(clock.GetUtcNow()));
        var phrase = personality.Spontaneous(now, !quiet && !fullscreen);
        if (phrase is not null) PresentDialogue(phrase);
    }

    private void OnCompanionSettings()
    {
        if (!owner.Dispatcher.CheckAccess()) { owner.Dispatcher.BeginInvoke(OnCompanionSettings); return; }
        if (!disposed && companion?.Active == true) HideBubble();
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
            owner.Dispatcher.BeginInvoke(() => { if (!disposed) { HideBubble(); Poll(true); } });
    }

    private void OnChanged()
    {
        if (disposed) return;
        if (!owner.Dispatcher.CheckAccess()) { owner.Dispatcher.BeginInvoke(OnChanged); return; }
        if (scheduler.PersistenceError is null)
        {
            lastNotifiedError = null;
            if (bubble.IsStoreNotice) HideBubble();
        }
        if (companion?.Active != true && bubble.Occurrences.Count > 0)
        {
            var latest = scheduler.GetPresentation();
            if (!latest.Any(o => scheduler.MayNotify(o, clock.GetUtcNow()))) HideBubble();
            else bubble.RefreshItems(latest);
            bubble.ShowPersistenceError(scheduler.PersistenceError);
        }
        if (bubble.IsDialogue && (!personality.Enabled || personality.IsQuiet(
                scheduler.LocalNow(clock.GetUtcNow()), scheduler.IsQuiet(clock.GetUtcNow())))) HideBubble();
        center?.RefreshToday();
    }

    private void OnOperationFailed(string error)
    {
        if (disposed) return;
        if (!owner.Dispatcher.CheckAccess()) { owner.Dispatcher.BeginInvoke(() => OnOperationFailed(error)); return; }
        if (bubble.IsVisible && !bubble.IsStoreNotice) bubble.ShowPersistenceError(error);
        if (lastNotifiedError != error)
        {
            logger.LogWarning("A reminder operation could not be persisted; corrective details are shown in the reminder UI.");
            lastNotifiedError = error;
            if (!bubble.IsVisible || bubble.IsStoreNotice) bubble.PresentStoreNotice(error);
            autoHide.Interval = TimeSpan.FromSeconds(45);
            autoHide.Start();
        }
        center?.RefreshToday();
    }

    private void Present(IReadOnlyList<ReminderOccurrence> occurrences)
    {
        companion?.Close(); // An explicit recurring-reminder preview can preempt a task bubble.
        // The new group is already recorded at the command boundary. Clear only the old visual.
        autoHide.Stop();
        bubble.HideReminder();
        var now = clock.GetUtcNow();
        string text = personality.ReminderMessage(occurrences[0],
            !personality.IsQuiet(scheduler.LocalNow(now), scheduler.IsQuiet(now)));
        personality.RecordSpeech(now);
        bubble.Present(occurrences, text);
        autoHide.Interval = TimeSpan.FromSeconds(45);
        autoHide.Start();
    }

    private void HideBubble()
    {
        autoHide.Stop();
        companion?.Close();
        // A save callback can already have selected a newer group while an older visual is closing.
        scheduler.ClosePresentation(bubble.Occurrences.Select(o => o.Id).ToArray());
        bubble.HideReminder();
    }

    private void PresentDialogue(string text)
    {
        bubble.PresentDialogue(personality.MoodLabel, text);
        autoHide.Interval = TimeSpan.FromSeconds(12);
        autoHide.Start();
    }

    private bool MayChat(DateTimeOffset now, bool replaceDialogue = false) => !interactionBlocked() && (!bubble.IsVisible || replaceDialogue && bubble.IsDialogue) &&
        !personality.IsQuiet(scheduler.LocalNow(now), scheduler.IsQuiet(now)) && !DesktopActivity.IsFullscreenApp() &&
        !scheduler.HasWaitingNotification(now);

    public void Pet(bool requested = false)
    {
        var now = clock.GetUtcNow();
        personality.Pet(now);
        var phrase = personality.PetReply(now, MayChat(now, requested), requested);
        if (requested && phrase is null && personality.Enabled && MayChat(now, true) && now - lastManualPetAt < TimeSpan.FromSeconds(3))
            phrase = lastManualPetReply; // Coalesce rapid menu gestures into the same visible response.
        else if (requested && phrase is not null) { lastManualPetReply = phrase; lastManualPetAt = now; }
        if (phrase is not null) PresentDialogue(phrase);
    }

    public void RequestPhrase(bool random)
    {
        var now = clock.GetUtcNow();
        var phrase = personality.RequestedPhrase(now, MayChat(now, true), random);
        if (phrase is not null) PresentDialogue(phrase);
    }

    public void TogglePersonality()
    {
        var options = scheduler.GetPersonalityOptions();
        options.PersonalityEnabled = !options.PersonalityEnabled;
        if (scheduler.UpdatePersonality(options).Success) personality.DeferSpontaneous(clock.GetUtcNow());
        if (!personality.Enabled && bubble.IsDialogue) HideBubble();
    }

    public void SuspendPersonality()
    {
        if (bubble.IsDialogue) HideBubble();
    }

    private void OnBubbleAction(ReminderOccurrence occurrence, string action)
    {
        if (occurrence.ReminderId == "task-companion") { if (companion is not null) _ = companion.ActAsync(bubble, action); return; }
        if (action == "snooze") scheduler.Snooze(occurrence.Id, clock.GetUtcNow());
        else scheduler.Acknowledge(occurrence.Id, action == "done", clock.GetUtcNow());
    }

    public void ShowTest(string reminderId = "test-break")
    {
        var now = clock.GetUtcNow();
        var occurrences = scheduler.AddTestGroup(reminderId, now);
        if (occurrences is null) return;
        // Explicit previews are labeled test records. They do not set LastFiredLocalDate,
        // complete real occurrences, or bypass quiet hours for automatic notifications.
        if (scheduler.MarkPresented(occurrences, now).Success) Present(occurrences);
    }

    public void ShowCenter(bool settings)
    {
        if (center is null)
        {
            center = new ReminderCenter(scheduler, ShowOccurrence, clock);
            center.Closed += (_, _) => center = null;
        }
        center.SelectTab(settings);
        if (center.WindowState == WindowState.Minimized) center.WindowState = WindowState.Normal;
        center.Show();
        new WindowPositionService(center).FitToWorkArea();
        center.Activate(); // Only this explicit menu action opens a focused editor.
    }

    private void ShowOccurrence(ReminderOccurrence occurrence)
    {
        var now = clock.GetUtcNow();
        if (!scheduler.MayNotify(occurrence, now)) return;
        var group = scheduler.PresentationGroup(occurrence, now);
        if (scheduler.MarkPresented(group, now).Success) Present(group);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        tick.Stop();
        autoHide.Stop();
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        scheduler.Changed -= OnChanged;
        scheduler.OperationFailed -= OnOperationFailed;
        center?.Close();
        if (companionSettings is not null) companionSettings.Changed -= OnCompanionSettings;
        companion?.Dispose();
        bubble.Dispose();
    }
}
