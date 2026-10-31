using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace ChihDesktop;

public sealed class CatWindow : Window
{
    private readonly CatBehaviorService behavior = new();
    private readonly AnimationService animation = new();
    private readonly WindowPositionService positioning;
    private readonly DispatcherTimer timer;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly PetConfiguration configuration;
    private double lastTime;
    private bool pressed, dragging, menuOpen;
    private bool renderingWalk;
    private TimeSpan lastRenderTime = TimeSpan.MinValue;
    private WindowPositionService.NativePoint pressCursor, pressPosition;
    private ReminderController? reminders;
    private bool applicationServicesStopped;
    private readonly AdminWindowManager admin;
    private readonly UserSettingsWindowManager userWindows;
    private readonly UserSettingsService userSettings;
    private readonly IReminderCommands reminderCommands;

    public CatWindow(PetConfiguration config, ReminderControllerFactory reminderFactory, AdminWindowManager adminWindows,
        UserSettingsWindowManager userWindows, UserSettingsService userSettings, IReminderCommands reminderCommands)
    {
        admin = adminWindows;
        this.userWindows = userWindows; this.userSettings = userSettings; this.reminderCommands = reminderCommands;
        userSettings.Changed += ApplyUserSettings;
        configuration = config;
        Title = "ChihDesktop";
        Width = config.CatSize; Height = config.CatSize * 130 / 160;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = config.AlwaysOnTop;
        Content = animation;
        positioning = new(this);
        ContextMenu = BuildMenu();
        MouseLeftButtonDown += OnPress;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnRelease;
        LostMouseCapture += (_, _) => { pressed = false; dragging = false; };
        timer = new DispatcherTimer(DispatcherPriority.Background)
        { Interval = TimeSpan.FromSeconds(1.0 / config.AnimationFps) };
        timer.Tick += OnTick;
        SourceInitialized += (_, _) => positioning.Reset();
        Loaded += (_, _) =>
        {
            lastTime = clock.Elapsed.TotalSeconds;
            timer.Start();
            reminders = reminderFactory.Create(this, () => pressed || dragging || menuOpen, () => behavior.State);
        };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(() => positioning.Clamp());
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        Closed += (_, _) => StopApplicationServices();
    }

    internal void StopApplicationServices()
    {
        if (applicationServicesStopped) return;
        applicationServicesStopped = true;
        timer?.Stop(); // Also safe if startup failed before the constructor completed.
        reminders?.Dispose();
        admin.Dispose();
        userWindows.Dispose(); userSettings.Changed -= ApplyUserSettings;
        CompositionTarget.Rendering -= OnWalkingRender;
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
    }

    private void OnDisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() => positioning.Clamp());
    private void ApplyUserSettings()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(ApplyUserSettings); return; }
        Topmost = configuration.AlwaysOnTop = userSettings.GetSnapshot().PetTopmost;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        // Walking advances on display frames. The low-frequency dispatcher timer still
        // handles resting states, input suspension and starting the next activity.
        if (renderingWalk) return;
        Advance();
    }

    private void OnWalkingRender(object? sender, EventArgs e)
    {
        if (e is RenderingEventArgs render)
        {
            if (render.RenderingTime == lastRenderTime) return;
            lastRenderTime = render.RenderingTime;
        }
        Advance();
    }

    private void Advance()
    {
        double now = clock.Elapsed.TotalSeconds;
        double dt = Math.Clamp(now - lastTime, 0, .1);
        lastTime = now;
        if (pressed || menuOpen) { SynchronizeWalkClock(); return; }
        behavior.Update(dt);
        if (!behavior.IsPaused && behavior.State == CatState.Walking)
        {
            double step = configuration.WalkingSpeed * dt * (behavior.Direction == CatDirection.Right ? 1 : -1);
            if (positioning.Walk(step)) behavior.TurnAround();
        }
        if (!behavior.IsPaused) animation.Update(behavior.State, behavior.Direction, dt);
        SynchronizeWalkClock();
    }

    private void SynchronizeWalkClock()
    {
        bool shouldRender = behavior.State == CatState.Walking && !behavior.IsPaused && !pressed && !menuOpen;
        if (shouldRender == renderingWalk) return;
        renderingWalk = shouldRender;
        if (renderingWalk)
        {
            lastRenderTime = TimeSpan.MinValue;
            CompositionTarget.Rendering += OnWalkingRender;
        }
        else CompositionTarget.Rendering -= OnWalkingRender;
    }

    private void OnPress(object sender, MouseButtonEventArgs e)
    {
        pressCursor = WindowPositionService.Cursor();
        pressPosition = positioning.Position();
        pressed = true; dragging = false;
        reminders?.SuspendPersonality();
        CaptureMouse();
        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!pressed) return;
        var cursor = WindowPositionService.Cursor();
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        int dx = cursor.X - pressCursor.X, dy = cursor.Y - pressCursor.Y;
        if (!dragging && (Math.Abs(dx) > SystemParameters.MinimumHorizontalDragDistance * dpi.DpiScaleX ||
                          Math.Abs(dy) > SystemParameters.MinimumVerticalDragDistance * dpi.DpiScaleY)) dragging = true;
        if (dragging) positioning.MoveTo(pressPosition.X + dx, pressPosition.Y + dy);
    }

    private void OnRelease(object sender, MouseButtonEventArgs e)
    {
        if (!pressed) return;
        bool wasDragging = dragging;
        pressed = false; dragging = false;
        ReleaseMouseCapture();
        if (wasDragging) positioning.Clamp();
        else PetChih();
        e.Handled = true;
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        var pause = new MenuItem { Header = "Pause" };
        pause.Click += (_, _) => { behavior.IsPaused = !behavior.IsPaused; pause.Header = behavior.IsPaused ? "Resume" : "Pause"; };
        menu.Items.Add(pause);
        Add("Sleep", () => { behavior.Sleep(); animation.Update(behavior.State, behavior.Direction, 0); });
        Add("Wake Up", () => { behavior.WakeUp(); animation.Update(behavior.State, behavior.Direction, 0); });
        Add("Reset Position", positioning.Reset);
        menu.Items.Add(new Separator());
        Add("Планировщик Чиха…", () => AfterMenu(admin.Show));
        Add("Настройки Чиха…", () => AfterMenu(() => userWindows.Show()));
        Add("Напоминания — сегодня", () => reminders?.ShowCenter(false));
        Add("Настроить напоминания…", () => reminders?.ShowCenter(true));
        var reminderTests = new MenuItem { Header = "Test Reminder Now · Проверить напоминание" };
        menu.Items.Add(reminderTests);
        menu.Items.Add(new Separator());
        Add("Погладить Чиха", () => AfterMenu(PetChih));
        Add("Чего ты хочешь?", () => AfterMenu(() => reminders?.RequestPhrase(false)));
        var personalityToggle = new MenuItem { Header = "Характер Чиха", IsCheckable = true };
        personalityToggle.Click += (_, _) => reminders?.TogglePersonality();
        menu.Items.Add(personalityToggle);
        Add("Тест случайной фразы", () => AfterMenu(() => reminders?.RequestPhrase(true)));
        menu.Items.Add(new Separator());
        Add("Exit", Close);
        menu.Opened += (_, _) =>
        {
            menuOpen = true;
            reminderTests.Items.Clear();
            foreach (var definition in reminderCommands.GetSnapshot().Reminders)
            {
                var test = new MenuItem { Header = definition.Title };
                string id = definition.Id;
                test.Click += (_, _) => AfterMenu(() => reminders?.ShowTest(id));
                reminderTests.Items.Add(test);
            }
            reminderTests.IsEnabled = reminderTests.Items.Count > 0;
            reminders?.SuspendPersonality();
            personalityToggle.IsChecked = reminders?.PersonalityEnabled == true;
        };
        menu.Closed += (_, _) => menuOpen = false;
        return menu;
        void Add(string title, Action action)
        {
            var item = new MenuItem { Header = title };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
    }

    private void AfterMenu(Action action) => Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
    {
        // Wait for the menu to release its capture before attempting any dialogue.
        if (!menuOpen && !pressed && !dragging) action();
    });

    private void PetChih()
    {
        behavior.React();
        animation.Update(behavior.State, behavior.Direction, 0);
        reminders?.Pet();
    }
}
