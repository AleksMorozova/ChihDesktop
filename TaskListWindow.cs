using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ChihDesktop;

// A native, modeless fallback: no WebView2, browser or synchronous database work.
public sealed class TaskListWindow : Window
{
    private readonly IPlannerService planner;
    private readonly UserSettingsService settings;
    private readonly TimeProvider clock;
    private readonly StackPanel rows = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 12) };
    private readonly CheckBox upcoming = new() { Content = "Также будущие задачи и без срока", Margin = new(0, 8, 0, 8) };
    private readonly CancellationTokenSource lifetime = new();
    private bool loading, closed;
    public TaskListWindow(IPlannerService planner, UserSettingsService settings, TimeProvider clock, Action openAdmin)
    {
        this.planner = planner; this.settings = settings; this.clock = clock;
        AppBranding.Apply(this);
        Title = "Чих · Мои задачи"; Width = 570; Height = 620; MinWidth = 360; MinHeight = 300;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 14; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(247, 248, 245)); ShowInTaskbar = true;
        var root = new DockPanel { Margin = new(22) };
        var header = new StackPanel(); header.Children.Add(new TextBlock { Text = "Мои задачи 🐾", FontSize = 25, FontWeight = FontWeights.SemiBold });
        header.Children.Add(upcoming); header.Children.Add(status);
        var buttons = new WrapPanel();
        Button Add(string text, Action click) { var b = new Button { Content = text, Padding = new(9, 5, 9, 5), Margin = new(0, 0, 8, 8) }; b.Click += (_, _) => click(); return b; }
        buttons.Children.Add(Add("Обновить", () => _ = RefreshAsync())); buttons.Children.Add(Add("Полный планировщик…", openAdmin));
        header.Children.Add(buttons); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        root.Children.Add(new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Content = root;
        upcoming.Checked += (_, _) => _ = RefreshAsync(); upcoming.Unchecked += (_, _) => _ = RefreshAsync();
        Loaded += (_, _) => _ = RefreshAsync(); Activated += (_, _) => _ = RefreshAsync();
        Closed += (_, _) => { closed = true; lifetime.Cancel(); lifetime.Dispose(); };
    }
    public async Task RefreshAsync()
    {
        if (loading || closed) return;
        loading = true; status.Text = "Чих проверяет список…"; rows.IsEnabled = false;
        try
        {
            var result = await planner.GetTasksAsync(new(PlannerView.AllActive), lifetime.Token);
            if (closed) return;
            rows.Children.Clear();
            if (!result.Success) { status.Text = result.Message; return; }
            var preferences = settings.GetSnapshot(); var zone = AppTimeZones.Get(preferences.TimeZoneId);
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
            var items = result.Value!.Where(t => TaskCompanionPolicy.InDesktopList(t, today, clock.GetUtcNow(), upcoming.IsChecked == true)).ToArray();
            status.Text = items.Length == 0 ? "На сегодня всё. Значит, можно полежать. 🐾" : $"Ожидают внимания: {items.Length}";
            foreach (var task in items)
            {
                var row = new StackPanel { Margin = new(0, 8, 0, 10) };
                row.Children.Add(new TextBlock { Text = task.Title, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold });
                var reminder = task.RemindAtUtc is { } at ? $" · 🔔 {TimeZoneInfo.ConvertTime(task.SnoozedUntilUtc ?? at, zone):g} · {task.ReminderStatus}" : "";
                row.Children.Add(new TextBlock { Text = "Не выполнено · " + (task.DueDate?.ToString("dd.MM.yyyy") ?? "Без срока") + reminder, TextWrapping = TextWrapping.Wrap });
                var actions = new WrapPanel { Margin = new(0, 6, 0, 0) };
                void Action(string title, Func<Task<PlannerResult<PlannerTaskDto>>> command)
                {
                    var b = new Button { Content = title, Padding = new(8, 4, 8, 4), Margin = new(0, 0, 6, 0) };
                    b.Click += async (_, _) =>
                    {
                        rows.IsEnabled = false;
                        var changed = await command();
                        if (closed) return;
                        if (!changed.Success) { status.Text = changed.Message; rows.IsEnabled = true; }
                        else await RefreshAsync();
                    };
                    actions.Children.Add(b);
                }
                Action("Готово ✓", () => planner.CompleteTaskAsync(task.Id, lifetime.Token));
                if (task.RemindAtUtc is not null && planner is ITaskReminderCommands commands)
                {
                    Action("Через 10 мин", () => commands.ChangeReminderAsync(task.Id, "snooze", task.Revision, lifetime.Token));
                    Action("Скрыть напоминание", () => commands.ChangeReminderAsync(task.Id, "dismiss", task.Revision, lifetime.Token));
                }
                row.Children.Add(actions); rows.Children.Add(row);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!closed) status.Text = "Не удалось загрузить задачи. Нажми «Обновить»; если ошибка повторяется, перезапусти Чиха."; }
        finally { loading = false; if (!closed) rows.IsEnabled = true; }
    }
}

public sealed class TaskListWindowManager(IPlannerService planner, UserSettingsService settings, TimeProvider clock,
    AdminWindowManager admin, AdminDiagnostics diagnostics) : IDisposable
{
    private TaskListWindow? window;
    public void Show()
    {
        try
        {
            if (window is null) { window = new(planner, settings, clock, admin.Show); window.Closed += (_, _) => window = null; }
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            if (!window.IsVisible) window.Show();
            new WindowPositionService(window).FitToWorkArea(); window.Activate();
            diagnostics.Window("tasks.visible", window);
        }
        catch (Exception ex) { diagnostics.Write("tasks.open.failed", error: ex); MessageBox.Show("Не удалось открыть задачи. Проверь журнал диагностики Чиха.", "ChihDesktop"); }
    }
    public void Dispose() { window?.Close(); window = null; }
}
