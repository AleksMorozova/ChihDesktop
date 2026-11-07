using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ChihDesktop;

namespace ChihDesktop.Mac;

internal sealed class MacPetWindow : Window
{
    private readonly CatBehaviorService behavior = new();
    private readonly SpriteView sprite = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1.0 / 30) };
    private double lastTick, frameElapsed, walkX;
    private CatState renderedState;
    private bool pressed, dragged, menuOpen;
    private bool closed;
    private PixelPoint pressScreen, pressWindow;

    public MacPetWindow()
    {
        Title = "Chih · macOS prototype";
        Width = 160 * PetDisplaySize.Scale;
        Height = Width * PetDisplaySize.HeightPerWidth;
        CanResize = false;
        WindowDecorations = WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Topmost = true; ShowInTaskbar = false; ShowActivated = false;
        using (var icon = PrototypeModel.Resource("Branding.chih-icon.png")) Icon = new WindowIcon(icon);
        Content = sprite;
        ContextMenu = CreateMenu();
        PointerPressed += Press;
        PointerMoved += Move;
        PointerReleased += Release;
        PointerCaptureLost += (_, _) => { pressed = false; dragged = false; };
        timer.Tick += (_, _) => Tick();
        Opened += (_, _) =>
        {
            ResetPosition(); lastTick = clock.Elapsed.TotalSeconds; timer.Start();
            Screens.Changed += DisplaysChanged;
            Console.WriteLine($"Pet opened: transparency={ActualTransparencyLevel}; scale={RenderScaling}; displays={Screens.All.Count}");
            if (ActualTransparencyLevel != WindowTransparencyLevel.Transparent)
                Console.Error.WriteLine("The compositor did not grant transparent rendering. Verify this backend on a real Mac.");
        };
        Closed += (_, _) => { closed = true; timer.Stop(); Screens.Changed -= DisplaysChanged; sprite.Dispose(); Console.WriteLine("Pet closed."); };
        sprite.Set(behavior.State, behavior.Direction, 0);
    }

    private void DisplaysChanged(object? sender, EventArgs e) => ClampToScreen();
    private Screen? CurrentScreen() => Screens.ScreenFromWindow(this) ?? Screens.Primary ?? Screens.All.FirstOrDefault();
    private void ResetPosition()
    {
        var screen = Screens.Primary ?? CurrentScreen();
        if (screen is null) return;
        var work = screen.WorkingArea;
        Position = new PixelPoint((int)(work.X + work.Width * .6), work.Bottom - (int)Math.Ceiling(Height * screen.Scaling) - 8);
        ClampToScreen();
    }
    private void ClampToScreen()
    {
        if (CurrentScreen() is not { } screen) return;
        var area = screen.WorkingArea;
        Position = new PixelPoint((int)PrototypeModel.Clamp(Position.X, area.X, area.Width, Width * RenderScaling),
            (int)PrototypeModel.Clamp(Position.Y, area.Y, area.Height, Height * RenderScaling));
        walkX = Position.X;
    }
    private void Tick()
    {
        if (closed) return;
        double now = clock.Elapsed.TotalSeconds;
        double delta = Math.Clamp(now - lastTick, 0, .1); lastTick = now; // No teleport on resume/stall.
        if (pressed || menuOpen) return;
        behavior.Update(delta);
        if (renderedState != behavior.State) { renderedState = behavior.State; frameElapsed = 0; }
        if (!behavior.IsPaused) frameElapsed += delta;
        if (behavior.State == CatState.Walking && !behavior.IsPaused && CurrentScreen() is { } screen)
        {
            double next = walkX + delta * 70 * RenderScaling * (behavior.Direction == CatDirection.Right ? 1 : -1);
            double clamped = PrototypeModel.Clamp(next, screen.WorkingArea.X, screen.WorkingArea.Width, Width * RenderScaling);
            if (next != clamped) behavior.TurnAround();
            walkX = clamped;
            Position = new PixelPoint((int)Math.Round(walkX), Position.Y);
        }
        else walkX = Position.X;
        sprite.Set(behavior.State, behavior.Direction, PrototypeModel.Frame(behavior.State, frameElapsed));
    }
    private void Press(object? sender, PointerPressedEventArgs e)
    {
        var buttons = e.GetCurrentPoint(this).Properties;
        bool controlClick = OperatingSystem.IsMacOS() && buttons.IsLeftButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (buttons.IsRightButtonPressed || controlClick)
        {
            pressed = dragged = false; e.Pointer.Capture(null);
            if (controlClick) { ContextMenu?.Open(this); e.Handled = true; }
            return;
        }
        if (!buttons.IsLeftButtonPressed) return;
        pressed = true; dragged = false;
        pressScreen = this.PointToScreen(e.GetPosition(this)); pressWindow = Position;
        e.Pointer.Capture(this); e.Handled = true;
    }
    private void Move(object? sender, PointerEventArgs e)
    {
        if (!pressed) return;
        var point = this.PointToScreen(e.GetPosition(this));
        var dx = point.X - pressScreen.X; var dy = point.Y - pressScreen.Y;
        dragged |= Math.Abs(dx) > 4 * RenderScaling || Math.Abs(dy) > 4 * RenderScaling;
        if (dragged) Position = new PixelPoint(pressWindow.X + dx, pressWindow.Y + dy);
    }
    private void Release(object? sender, PointerReleasedEventArgs e)
    {
        if (!pressed) return;
        bool wasDrag = dragged; pressed = false; dragged = false; e.Pointer.Capture(null);
        if (wasDrag) ClampToScreen(); else React();
        e.Handled = true;
    }
    private void React()
    {
        behavior.React(); frameElapsed = 0; sprite.Set(behavior.State, behavior.Direction, 0);
    }
    private ContextMenu CreateMenu()
    {
        var menu = new ContextMenu(); var items = new List<object>();
        MenuItem Add(string title, Action action)
        {
            var item = new MenuItem { Header = title };
            item.Click += (_, _) =>
            {
                void Execute()
                {
                    if (closed) return;
                    action();
                    if (!closed) { frameElapsed = 0; sprite.Set(behavior.State, behavior.Direction, 0); }
                }
                if (!menuOpen && !menu.IsOpen) Dispatcher.UIThread.Post(Execute);
                else
                {
                    EventHandler<RoutedEventArgs>? closed = null;
                    closed = (_, _) => { menu.Closed -= closed; Dispatcher.UIThread.Post(Execute); };
                    menu.Closed += closed;
                }
            };
            items.Add(item); return item;
        }
        var pause = Add("Пауза / продолжить", () => behavior.IsPaused = !behavior.IsPaused);
        Add("Спать", behavior.Sleep); Add("Проснуться", behavior.WakeUp); Add("Погладить", React);
        Add("Вернуть на экран", ResetPosition);
        Add("Поверх окон: переключить", () => Topmost = !Topmost);
        items.Add(new MenuItem { Header = "Прототип · задачи и напоминания не подключены", IsEnabled = false });
        Add("Выход", Close);
        menu.ItemsSource = items;
        menu.Opened += (_, _) => { menuOpen = true; pressed = dragged = false; pause.Header = behavior.IsPaused ? "Продолжить" : "Пауза"; };
        menu.Closed += (_, _) => menuOpen = false;
        return menu;
    }
}

internal sealed class SpriteView : Control, IDisposable
{
    private readonly Dictionary<CatState, Bitmap[]> clips = new();
    private CatState state;
    private CatDirection direction;
    private int frame;
    public SpriteView()
    {
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
        foreach (var clip in PrototypeModel.Clips)
            clips.Add(clip.State, Enumerable.Range(0, clip.Count).Select(i =>
            {
                using var stream = PrototypeModel.Resource($"Sprites.{clip.Name}-{i:00}.png");
                return new Bitmap(stream);
            }).ToArray());
    }
    public void Set(CatState next, CatDirection facing, int index)
    {
        if (state == next && direction == facing && frame == index) return;
        state = next; direction = facing; frame = index; InvalidateVisual();
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        using var mirror = context.PushTransform(direction == CatDirection.Left ? new Matrix(-1, 0, 0, 1, Bounds.Width, 0) : Matrix.Identity);
        context.DrawImage(clips[state][frame], new Rect(Bounds.Size));
    }
    public void Dispose() { foreach (var image in clips.Values.SelectMany(v => v)) image.Dispose(); }
}
