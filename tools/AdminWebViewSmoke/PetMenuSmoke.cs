using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ChihDesktop;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Web.WebView2.Wpf;

internal static class PetMenuSmoke
{
    public static async Task Run(IHost host, Application app)
    {
        var settings = host.Services.GetRequiredService<UserSettingsService>();
        var preferences = settings.GetSnapshot(); var personality = preferences.Personality;
        personality.QuietHoursEnabled = false;
        Check(settings.Save(preferences with { PetTopmost = true, Personality = personality, OnboardingCompleted = true }).Success, "Isolated menu preferences");
        var reminders = host.Services.GetRequiredService<IReminderCommands>();
        Check(reminders.UpdateSettings([], false, new(22, 0), new(8, 0)).Success, "Isolated menu quiet hours");
        foreach (var title in new[] { "Synthetic A", "Synthetic B" })
        {
            var seed = reminders.CreateReminder(new() { Title = title, Time = new(20, 0), BubbleGroup = "synthetic-pair" });
            Check(seed.Success, "Seed synthetic preview group: " + seed.Error);
        }
        Check(reminders.CreateReminder(new() { Title = "Synthetic break", Recurrence = ReminderRecurrence.RunningInterval }).Success, "Seed separate break preview");
        var clock = (Program.SmokeClock)host.Services.GetRequiredService<TimeProvider>();
        var diagnostics = host.Services.GetRequiredService<AdminDiagnostics>();
        var cat = host.Services.GetRequiredService<CatWindow>(); cat.Show();
        await Wait(() => app.Windows.OfType<ReminderBubble>().Any(), "Actual CatWindow loaded with presenter");
        var dpi = VisualTreeHelper.GetDpi(cat);
        Check(Math.Abs(cat.ActualWidth - 132) <= 1 / dpi.DpiScaleX && Math.Abs(cat.ActualHeight - 107.25) <= 1 / dpi.DpiScaleY,
            $"Default pet window is 17.5% smaller within native pixel rounding ({cat.ActualWidth:F2} × {cat.ActualHeight:F2})");
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var sprite = (AnimationService)cat.Content;
        var rendered = new RenderTargetBitmap((int)Math.Ceiling(sprite.ActualWidth), (int)Math.Ceiling(sprite.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        rendered.Render(sprite);
        int stride = rendered.PixelWidth * 4;
        var pixels = new byte[stride * rendered.PixelHeight];
        rendered.CopyPixels(pixels, stride, 0);
        var alpha = Enumerable.Range(0, pixels.Length / 4).Select(i => pixels[i * 4 + 3]);
        int minAlpha = alpha.Min(), maxAlpha = alpha.Max();
        Check(minAlpha == 0 && maxAlpha >= 240, $"Scaled sprite renders with transparent padding and visible artwork (alpha {minAlpha}–{maxAlpha})");
        var bubble = app.Windows.OfType<ReminderBubble>().Single();
        int Completed() => File.Exists(diagnostics.LogPath) ? File.ReadLines(diagnostics.LogPath!).Count(line => JsonDocument.Parse(line).RootElement.GetProperty("stage").GetString() == "pet.command.complete") : 0;
        async Task Click(string title, bool preview = false)
        {
            var menu = cat.ContextMenu!; menu.PlacementTarget = cat; menu.IsOpen = true;
            await Wait(() => menu.IsOpen, "Actual WPF context menu opened");
            var items = menu.Items.OfType<MenuItem>();
            var item = preview ? items.Single(i => i.Header.ToString()!.StartsWith("Test Reminder Now")).Items.OfType<MenuItem>().Single(i => i.Header.ToString() == title)
                : items.Single(i => i.Header.ToString() == title);
            int before = Completed(); item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await Task.Delay(35); Check(Completed() == before, "Command waits for actual Closed: " + title);
            menu.IsOpen = false; // Exercise the actual WPF popup closing animation and Closed event.
            await Wait(() => Completed() == before + 1, "Command executed: " + title);
        }
        await Click("Pause"); Check(cat.MenuState.Paused, "Pause freezes behavior");
        await Click("Resume"); Check(!cat.MenuState.Paused, "Resume restores behavior");
        await Click("Sleep"); Check(cat.MenuState.State == CatState.Sleeping, "Sleep state visible");
        await Click("Wake Up"); Check(cat.MenuState.State == CatState.Stretching, "Wake transitions through stretching");
        cat.Left = -10000; cat.Top = -10000; await Click("Reset Position");
        Check(double.IsFinite(cat.Left) && cat.Left >= 0 && cat.Top >= 0, "Reset restores primary-monitor position");

        await Click("Open Admin / Планировщик Чиха…");
        var admin = app.Windows.OfType<AdminWindow>().Single();
        await Wait(() => Descendants<WebView2>(admin).Any(w => AdminNavigationPolicy.IsLocal(w.CoreWebView2?.Source)), "Real menu reached local WebView2 navigation");
        Check(cat.Topmost && !admin.Topmost && admin.IsActive && cat.Width < 400, "Normal admin activates alongside small topmost pet");
        await Click("Open Admin / Планировщик Чиха…"); Check(app.Windows.OfType<AdminWindow>().Count() == 1 && admin.IsVisible, "Admin reused visibly");
        admin.WindowState = WindowState.Minimized; await Click("Open Admin / Планировщик Чиха…"); Check(admin.WindowState == WindowState.Normal, "Admin restored from minimized");
        admin.Close(); await Click("Open Admin / Планировщик Чиха…"); Check(app.Windows.OfType<AdminWindow>().Count() == 1, "Admin closes and reopens");
        app.Windows.OfType<AdminWindow>().Single().Close();
        foreach (string title in new[] { "Мои задачи…", "Настройки Чиха…" })
        {
            await Click(title); await Click(title);
            Window window = title == "Мои задачи…" ? app.Windows.OfType<TaskListWindow>().Single() : app.Windows.OfType<UserSettingsWindow>().Single();
            Check(window.IsVisible && window.Icon is not null, "Visible branded window: " + title);
            window.WindowState = WindowState.Minimized; await Click(title);
            Check(window.WindowState == WindowState.Normal, "Restored normal window: " + title);
            window.Close(); await Click(title);
            window = title == "Мои задачи…" ? app.Windows.OfType<TaskListWindow>().Single() : app.Windows.OfType<UserSettingsWindow>().Single(); window.Close();
        }
        await Click("Напоминания — сегодня"); await Click("Напоминания — сегодня");
        var center = app.Windows.OfType<ReminderCenter>().Single(); Check(center.IsVisible && center.ShowInTaskbar, "Reminder center normal visible taskbar window");
        center.WindowState = WindowState.Minimized; await Click("Напоминания — сегодня");
        Check(center.WindowState == WindowState.Normal, "Reminder center restored from minimized");
        await Click("Настроить напоминания…"); Check(Descendants<TabControl>(center).Single().SelectedIndex == 1, "Reminder settings tab selected");
        center.Close(); await Click("Напоминания — сегодня"); app.Windows.OfType<ReminderCenter>().Single().Close();

        cat.Activate(); await Click("Sleep"); await Click("Погладить Чиха");
        Check(cat.MenuState.State == CatState.Reacting && bubble.IsVisible && bubble.IsDialogue, "Explicit pet wakes safely and displays dialogue");
        string first = string.Join(" ", Descendants<TextBlock>(bubble).Select(t => t.Text));
        await Click("Погладить Чиха"); Check(bubble.IsVisible, "Rapid pet coalesces to one visible bubble");
        clock.Now = clock.Now.AddSeconds(4); await Click("Погладить Чиха");
        Check(first != string.Join(" ", Descendants<TextBlock>(bubble).Select(t => t.Text)), "Petting dialogue varies");
        clock.Now = clock.Now.AddSeconds(4); await Click("Чего ты хочешь?"); Check(bubble.IsDialogue, "Requested phrase visible");
        clock.Now = clock.Now.AddSeconds(4); await Click("Тест случайной фразы"); Check(bubble.IsDialogue, "Random test phrase visible");
        await Click("Характер Чиха"); Check(!reminders.GetPersonalityOptions().PersonalityEnabled, "Personality disabled durably");
        await Click("Погладить Чиха"); Check(!bubble.IsVisible, "Personality off respected");
        await Click("Характер Чиха"); Check(reminders.GetPersonalityOptions().PersonalityEnabled, "Personality enabled durably");
        await Click("Synthetic A", true); Check(bubble.Occurrences.Count == 2 && bubble.Occurrences.All(o => o.IsManualTest), "Grouped preview preserves separate confirmations");
        clock.Now = clock.Now.AddSeconds(4); await Click("Погладить Чиха"); Check(bubble.Occurrences.Count == 2, "Reminder has priority over petting");
        await Click("Synthetic break", true); Check(bubble.Occurrences.Count == 1, "Separate break preview");
        Check(reminders.GetSnapshot().Reminders.All(d => d.LastFiredLocalDate is null), "Previews do not fire recurring schedule");
        await Click("Exit"); Check(!cat.IsVisible && !app.Windows.OfType<CatWindow>().Any(), "Exit closes pet and owned services/windows");
        Check(!File.ReadAllText(diagnostics.LogPath!).Contains("pet.command.failed"), "No context-menu exceptions");
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static async Task Wait(Func<bool> predicate, string label)
    {
        var limit = DateTime.UtcNow.AddSeconds(25);
        while (!predicate()) { if (DateTime.UtcNow > limit) throw new TimeoutException(label); await Task.Delay(50); }
        Check(true, label);
    }
    private static void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException(label); Console.WriteLine("PASS: " + label); }
}
