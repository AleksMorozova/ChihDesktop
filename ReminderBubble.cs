using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace ChihDesktop;

public sealed class ReminderBubble : Window, IReminderPresenter
{
    private readonly Window pet;
    private readonly TextBlock title = new() { FontWeight = FontWeights.SemiBold, FontSize = 16, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 12) };
    private readonly StackPanel reminderItems = new();
    private readonly TextBlock persistenceError = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap,
        Visibility = Visibility.Collapsed, Margin = new Thickness(0, 5, 0, 5) };
    private readonly TextBlock heading = new() { Foreground = Brushes.Teal, Margin = new Thickness(0, 0, 0, 5) };
    private readonly TextBlock footer = new() { FontSize = 11, Foreground = Brushes.DimGray,
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 0) };
    public IReadOnlyList<ReminderOccurrence> Occurrences { get; private set; } = [];
    public event Action<ReminderOccurrence, string>? ActionRequested;
    public event Action? DialogueDismissRequested;
    public bool IsStoreNotice { get; private set; }
    public bool IsDialogue => IsVisible && Occurrences.Count == 0 && !IsStoreNotice;

    public ReminderBubble(Window pet)
    {
        AppBranding.Apply(this);
        this.pet = pet;
        Owner = pet;
        Width = 370;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = pet.Topmost;
        var panel = new StackPanel();
        panel.Children.Add(heading);
        panel.Children.Add(title);
        panel.Children.Add(message);
        panel.Children.Add(reminderItems);
        panel.Children.Add(persistenceError);
        var close = new Button { Content = "Закрыть", HorizontalAlignment = HorizontalAlignment.Right, Focusable = false,
            Padding = new Thickness(7, 4, 7, 4), Margin = new Thickness(0, 6, 0, 0) };
        close.Click += (_, _) => DialogueDismissRequested?.Invoke();
        panel.Children.Add(close);
        panel.Children.Add(footer);
        Content = new Border { Background = new SolidColorBrush(Color.FromRgb(255, 250, 236)), BorderBrush = Brushes.BurlyWood,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18, 18, 18, 3), Padding = new Thickness(16), Child = panel };
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            long style = IntPtr.Size == 8 ? GetWindowLongPtr(handle, -20).ToInt64() : GetWindowLong(handle, -20);
            if (IntPtr.Size == 8) SetWindowLongPtr(handle, -20, new nint(style | 0x08000000L | 0x80));
            else SetWindowLong(handle, -20, (int)(style | 0x08000000L | 0x80));
            HwndSource.FromHwnd(handle)?.AddHook(NoActivate);
        };
        SizeChanged += (_, _) => FollowPet();
        pet.LocationChanged += OnPetMoved;
        pet.SizeChanged += OnPetResized;
        Closed += (_, _) => { pet.LocationChanged -= OnPetMoved; pet.SizeChanged -= OnPetResized; };
    }

    private Button ActionButton(ReminderOccurrence occurrence, string label, string action)
    {
        var button = new Button { Content = label, Padding = new Thickness(7, 5, 7, 5), Margin = new Thickness(0, 0, 5, 4), Focusable = false };
        button.Click += (_, _) =>
        {
            ActionRequested?.Invoke(occurrence, action);
        };
        return button;
    }

    private static nint NoActivate(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x21) { handled = true; return 3; } // WM_MOUSEACTIVATE / MA_NOACTIVATE
        return 0;
    }

    public void Present(IReadOnlyList<ReminderOccurrence> occurrences, string displayMessage)
    {
        reminderItems.IsEnabled = true;
        IsStoreNotice = false;
        persistenceError.Visibility = Visibility.Collapsed;
        Occurrences = occurrences;
        bool preview = occurrences.Any(o => o.IsManualTest);
        title.Text = string.Join(" + ", occurrences.Select(o => o.Title));
        heading.Text = preview ? "ТЕСТ · не реальный приём 🐾" : "Чих напоминает 🐾";
        message.Text = displayMessage;
        footer.Text = preview ? "Отметки относятся только к тесту. Реальное расписание не меняется."
            : "Каждый пункт отмечается отдельно. Закрытие не означает выполнение.";
        footer.Visibility = Visibility.Visible;
        RefreshItems();
        Show();
        UpdateLayout();
        FollowPet();
    }

    public void AddTaskOpenAction(Action open)
    {
        var button = new Button { Content = "Открыть задачу / список", Focusable = false, Padding = new(7, 5, 7, 5) };
        button.Click += (_, _) => open(); reminderItems.Children.Add(button);
    }

    internal void SetTaskActionsEnabled(bool enabled) => reminderItems.IsEnabled = enabled;

    public void PresentDialogue(string mood, string text)
    {
        IsStoreNotice = false;
        persistenceError.Visibility = Visibility.Collapsed;
        Occurrences = [];
        heading.Text = "Чих на связи 🐾";
        title.Text = "Управляющий домом · " + mood;
        message.Text = text;
        reminderItems.Children.Clear();
        footer.Visibility = Visibility.Collapsed;
        Show();
        UpdateLayout();
        FollowPet();
    }

    public void RefreshItems(IReadOnlyList<ReminderOccurrence>? latest = null)
    {
        if (latest is not null) Occurrences = latest;
        reminderItems.Children.Clear();
        foreach (var occurrence in Occurrences)
        {
            var row = new StackPanel { Margin = new Thickness(0, 5, 0, 5) };
            if (occurrence.RequiresConfirmation)
            {
                var check = new CheckBox { Content = new TextBlock { Text = occurrence.Title + " — готово", TextWrapping = TextWrapping.Wrap },
                    Focusable = false, IsChecked = occurrence.Acknowledgment == ReminderAcknowledgment.Completed,
                    IsEnabled = occurrence.Acknowledgment != ReminderAcknowledgment.Completed, Margin = new Thickness(0, 0, 0, 4) };
                // Checking is an explicit per-item confirmation; closing or hiding never invokes it.
                check.Checked += (_, _) => ActionRequested?.Invoke(occurrence, "done");
                row.Children.Add(check);
            }
            string? state = occurrence.Acknowledgment switch
            {
                ReminderAcknowledgment.Completed => "Подтверждено тобой ✓",
                ReminderAcknowledgment.Dismissed => "Скрыто · выполнение не подтверждено",
                _ => occurrence.SnoozedUntilUtc is not null ? "Отложено на 10 минут" : null
            };
            if (state is not null) row.Children.Add(new TextBlock { Text = state, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray });
            if (occurrence.Acknowledgment == ReminderAcknowledgment.Pending && occurrence.SnoozedUntilUtc is null)
            {
                var actions = new WrapPanel();
                actions.Children.Add(ActionButton(occurrence, "Через 10 мин", "snooze"));
                actions.Children.Add(ActionButton(occurrence, "Скрыть пункт", "dismiss"));
                row.Children.Add(actions);
            }
            reminderItems.Children.Add(row);
        }
    }

    public void ShowPersistenceError(string? error)
    {
        persistenceError.Text = error;
        persistenceError.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
    }

    public void PresentStoreNotice(string text)
    {
        PresentDialogue("напоминания", text);
        IsStoreNotice = true;
        heading.Text = "Состояние напоминаний";
        title.Text = "Проверь сохранение";
    }

    public void HideReminder() { Hide(); Occurrences = []; IsStoreNotice = false; }
    public void Dispose() => Close();
    private void OnPetMoved(object? sender, EventArgs e) => FollowPet();
    private void OnPetResized(object sender, SizeChangedEventArgs e) => FollowPet();

    public void FollowPet()
    {
        if (!IsVisible) return;
        var handle = new WindowInteropHelper(this).Handle;
        var petHandle = new WindowInteropHelper(pet).Handle;
        if (handle == 0 || petHandle == 0) return;
        GetWindowRect(petHandle, out var cat);
        GetWindowRect(handle, out var bubble);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfo(MonitorFromWindow(petHandle, 2), ref info);
        int width = bubble.Right - bubble.Left, height = bubble.Bottom - bubble.Top;
        int x = cat.Right + 8;
        if (x + width > info.Work.Right) x = cat.Left - width - 8;
        x = Math.Clamp(x, info.Work.Left, Math.Max(info.Work.Left, info.Work.Right - width));
        int y = Math.Clamp(cat.Top - height + 40, info.Work.Top, Math.Max(info.Work.Top, info.Work.Bottom - height));
        SetWindowPos(handle, 0, x, y, 0, 0, 0x1 | 0x4 | 0x10);
    }

    [StructLayout(LayoutKind.Sequential)] private struct RectI { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public RectI Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out RectI rect);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(nint hwnd, int index, int value);
}
