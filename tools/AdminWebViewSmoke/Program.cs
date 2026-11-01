using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ChihDesktop;
using ChihDesktop.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

// Optional Windows integration test; never starts the real App or reads user reminders.
// Uses the actual AdminWindow, bridge, DI and SQLite, with isolated DB/browser-profile paths.
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--verify-profile") return VerifyProfile(args[1], args[2]);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "ChihDesktop.slnx"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Run the smoke test from the workspace.");
        string output = Path.Combine(root.FullName, "artifacts", "phase4-admin-fix");
        string isolated = Path.Combine(output, "webview-" + Guid.NewGuid().ToString("N"));
        string profile = Path.Combine(isolated, "profile");
        Directory.CreateDirectory(isolated);
        // Official WebView2 override, process-local; checked against the actual environment below.
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", profile);
        bool missingRuntime = args.Contains("--missing-runtime");
        if (missingRuntime)
            Environment.SetEnvironmentVariable("WEBVIEW2_BROWSER_EXECUTABLE_FOLDER", Path.Combine(isolated, "no-runtime"));
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            IHost? host = null; AdminWindow? window = null; AdminWindowManager? manager = null;
            int exit = 1;
            try
            {
                host = SmokeHost(isolated);
                if (args.Contains("--pet-menu")) { await PetMenuSmoke.Run(host, app); exit = 0; return; }
                var planner = host.Services.GetRequiredService<IPlannerService>();
                int nativeSettingsOpened = 0;
                AdminWindowManager NewManager() => new(planner, host!.Services.GetRequiredService<UserSettingsWindowManager>(), host.Services.GetRequiredService<AdminApplicationApi>(),
                    () => new AdminWindow(planner, () => nativeSettingsOpened++, host.Services.GetRequiredService<AdminApplicationApi>()) { ShowInTaskbar = false, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -2000, Top = -2000 });
                using (var native = new TaskListWindowManager(planner, host.Services.GetRequiredService<UserSettingsService>(), TimeProvider.System,
                    host.Services.GetRequiredService<AdminWindowManager>(), host.Services.GetRequiredService<AdminDiagnostics>()))
                {
                    native.Show(); native.Show();
                    Check(app.Windows.OfType<TaskListWindow>().Count() == 1, "Native tasks reuse a single window without WebView2");
                    var list = app.Windows.OfType<TaskListWindow>().Single();
                    Check(list.IsVisible && list.ShowInTaskbar, "Native task list is visible and in taskbar");
                    list.Close(); native.Show();
                    Check(app.Windows.OfType<TaskListWindow>().Count() == 1, "Native task list closes and reopens");
                }
                manager = NewManager(); manager.Show();
                window = app.Windows.OfType<AdminWindow>().Single();
                // Hide briefly while testing reactivation so an offscreen test window cannot take keyboard focus.
                window.Hide(); manager.Show();
                Check(app.Windows.OfType<AdminWindow>().Count() == 1, "Repeated Open Admin reuses one window");
                window.Show();
                var grid = (Grid)window.Content;
                var web = grid.Children.OfType<WebView2>().Single();
                if (missingRuntime)
                {
                    await Wait(() => Task.FromResult(grid.Children.OfType<Button>().Any()), "Missing-runtime native error panel");
                    Check(web.Visibility == Visibility.Collapsed, "Failed renderer is hidden");
                    Check(grid.Children.OfType<TextBlock>().Single().Text.Contains("WebView2 Runtime"), "Actionable runtime explanation");
                    grid.Children.OfType<Button>().Single(b => b.Content.ToString() == "Открыть нативные настройки").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(nativeSettingsOpened == 1, "Native settings remain accessible");
                    exit = 0;
                    return;
                }
                await Wait(async () => web.CoreWebView2 is not null && await Script<bool>(web, "!!document.querySelector('.task-panel') && !document.querySelector('.loading')"), "WebView2/frontend initialization");
                Check(AdminNavigationPolicy.IsLocal(web.CoreWebView2.Source), "Virtual-host origin");
                Check(web.CoreWebView2.Source.Contains("?build="), "Production entry-page cache fingerprint");
                Check(Path.GetFullPath(web.CoreWebView2.Environment.UserDataFolder) == Path.GetFullPath(profile), "Isolated WebView2 profile");
                Check(await Script<bool>(web, "!!window.chrome?.webview"), "Native bridge exists");
                Check(await Script<bool>(web, "!document.querySelector('.runtime-banner') && !document.querySelector('[role=alert]')"), "Real native mode without mock data/errors");
                Check(await Script<string>(web, "getComputedStyle(document.querySelector('.app-shell')).display") == "flex", "Production layout CSS");
                Check((await Script<string>(web, "getComputedStyle(document.documentElement).fontFamily")).Contains("Segoe UI"), "Production fonts");
                var reminders = host.Services.GetRequiredService<IReminderCommands>();
                Check(reminders.CreateReminder(new() { Id = "synthetic-untouched", Title = "Synthetic untouched schedule", Time = new(23, 15), Enabled = false }).Success, "Seed unrelated isolated schedule");
                string untouched = JsonSerializer.Serialize(reminders.GetSnapshot().Reminders.Single(), ReminderDataCodec.JsonOptions);

                await ClickText(web, ".nav-item", "Задачи");
                await Wait(async () => await Script<bool>(web, "!!document.querySelector('.filters') && !document.querySelector('.loading')"), "Tasks view");
                await web.ExecuteScriptAsync("document.querySelector('button.primary').click()");
                await Wait(async () => await Script<bool>(web, "!!document.querySelector('[role=dialog] input[name=title]')"), "Create dialog");
                await SetTitle(web, "Synthetic WebView task");
                await web.ExecuteScriptAsync("document.querySelector('[role=dialog] form').requestSubmit()");
                await Wait(async () => (await planner.GetTasksAsync()).Value?.Any(t => t.Title == "Synthetic WebView task") == true, "Create through JS/native bridge/SQLite");
                await Wait(async () => await Script<bool>(web, "!document.querySelector('[role=dialog]') && document.body.textContent.includes('Synthetic WebView task')"), "Created task displayed");
                await ClickAria(web, "Редактировать: Synthetic WebView task");
                await Wait(async () => await Script<bool>(web, "!!document.querySelector('[role=dialog] input[name=title]')"), "Task reminder edit dialog");
                await web.ExecuteScriptAsync("Array.from(document.querySelectorAll('[role=dialog] label')).find(e=>e.textContent.includes('Напомнить об этой задаче')).querySelector('input').click()");
                await SetInput(web, "[role=dialog] input[type=datetime-local]", "2050-12-01T12:00");
                await Wait(async () => await Script<bool>(web, "Array.from(document.querySelectorAll('[role=dialog] select')).some(e=>e.value==='UTC')"), "Task reminder has explicit UTC zone");
                await web.ExecuteScriptAsync("document.querySelector('[role=dialog] form').requestSubmit()");
                await Wait(async () => (await planner.GetTasksAsync()).Value?.Single().RemindAtUtc == DateTimeOffset.Parse("2050-12-01T12:00:00Z"), "React reminder resolves native timezone and persists SQLite independently of due date");
                await Wait(async () => await Script<bool>(web, "!document.querySelector('[role=dialog]')"), "Task reminder saved");
                manager.Dispose(); host.Dispose();
                Check(!app.Windows.OfType<AdminWindow>().Any(), "Admin close disposes window");
                await VerifyInFreshProcess(isolated, "task-created");
                host = SmokeHost(isolated); planner = host.Services.GetRequiredService<IPlannerService>(); reminders = host.Services.GetRequiredService<IReminderCommands>();
                Check((await planner.GetTasksAsync()).Value?.Single().Title == "Synthetic WebView task", "SQLite task survives host restart");
                using (var native = new TaskListWindowManager(planner, host.Services.GetRequiredService<UserSettingsService>(), TimeProvider.System,
                    host.Services.GetRequiredService<AdminWindowManager>(), host.Services.GetRequiredService<AdminDiagnostics>()))
                {
                    native.Show(); native.Show();
                    Check(app.Windows.OfType<TaskListWindow>().Count() == 1, "Native tasks reuse a single window without WebView2");
                    var list = app.Windows.OfType<TaskListWindow>().Single();
                    Check(list.IsVisible && list.ShowInTaskbar, "Native task list is visible and in taskbar");
                    list.Close(); native.Show();
                    Check(app.Windows.OfType<TaskListWindow>().Count() == 1, "Native task list closes and reopens");
                }
                manager = NewManager(); manager.Show(); window = app.Windows.OfType<AdminWindow>().Single();
                grid = (Grid)window.Content; web = grid.Children.OfType<WebView2>().Single();
                await Wait(async () => web.CoreWebView2 is not null && await Script<bool>(web, "!!document.querySelector('.task-panel') && !document.querySelector('.loading')"), "Reopened admin initializes real bridge");
                await ClickText(web, ".nav-item", "Задачи");
                await Wait(async () => await Script<bool>(web, "!!document.querySelector('.task-title') && document.body.textContent.includes('Synthetic WebView task')"), "Persisted task displayed after reopening");
                await ClickAria(web, "Редактировать: Synthetic WebView task");
                await Wait(async () => await Script<bool>(web, "!!document.querySelector('[role=dialog] input[name=title]')"), "Edit dialog");
                await SetTitle(web, "Synthetic edited WebView task");
                await web.ExecuteScriptAsync("document.querySelector('[role=dialog] form').requestSubmit()");
                await Wait(async () => (await planner.GetTasksAsync()).Value?.Any(t => t.Title == "Synthetic edited WebView task") == true, "Edit persisted");
                await Wait(async () => await Script<bool>(web, "!document.querySelector('[role=dialog]')"), "Edit finished");
                await ClickAria(web, "Завершить: Synthetic edited WebView task");
                await Wait(async () => (await planner.GetTasksAsync(new(PlannerView.Completed))).Value?.Count == 1, "Completion persisted");
                await Wait(async () => await Script<bool>(web, "!document.querySelector('.loading') && !document.querySelector('button.primary').disabled"), "Completion UI ready");
                await ClickText(web, ".filters button", "Завершённые");
                await Wait(async () => await Script<bool>(web, "!!document.querySelector('[aria-label=\"Вернуть: Synthetic edited WebView task\"]')"), "Completed query displayed");
                await ClickAria(web, "Вернуть: Synthetic edited WebView task");
                await Wait(async () => (await planner.GetTasksAsync()).Value?.Count == 1, "Reopen persisted");
                await Wait(async () => await Script<bool>(web, "!document.querySelector('.loading') && !document.querySelector('button.primary').disabled"), "Reopen UI ready");
                await ClickText(web, ".filters button", "Все активные");
                await Wait(async () => await Script<bool>(web, "!!document.querySelector('[aria-label=\"Удалить: Synthetic edited WebView task\"]')"), "Active query displayed");

                await using (var screenshot = File.Create(Path.Combine(output, "webview-smoke.png")))
                    await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, screenshot);
                await ClickAria(web, "Удалить: Synthetic edited WebView task");
                await Wait(async () => await Script<bool>(web, "!!document.querySelector('[role=dialog] .danger')"), "Delete confirmation");
                Check((await planner.GetTasksAsync()).Value?.Count == 1, "Opening confirmation did not delete");
                await web.ExecuteScriptAsync("document.querySelector('[role=dialog] .danger').click()");
                await Wait(async () => (await planner.GetTasksAsync()).Value?.Count == 0, "Confirmed deletion persisted");
                await ClickText(web, ".nav-item", "Напоминания");
                await ClickText(web, ".admin-actions button", "Новое напоминание");
                await SetInput(web, "input[name=reminderTitle]", "Synthetic WebView reminder");
                await ClickText(web, ".admin-editor button", "Сохранить напоминание");
                await Wait(() => Task.FromResult(reminders.GetSnapshot().Reminders.Count == 2), "React reminder creation saved to JSON");
                var definition = reminders.GetSnapshot().Reminders.Single(r => r.Id != "synthetic-untouched");
                Check(!definition.Enabled, "New reminders default to disabled");
                await Wait(async () => await Script<bool>(web, "!!document.querySelector('input[aria-label=\"Включено: Synthetic WebView reminder\"]:not(:disabled)')"), "Reminder editor refresh complete");
                await web.ExecuteScriptAsync("document.querySelector('input[aria-label=\"Включено: Synthetic WebView reminder\"]').click()");
                await Wait(() => Task.FromResult(reminders.GetSnapshot().Reminders.Single(r => r.Id == definition.Id).Enabled), "React enable persisted");
                await ClickAria(web, "Изменить напоминание: Synthetic WebView reminder");
                await SetInput(web, "input[name=reminderTitle]", "Synthetic edited reminder");
                await ClickText(web, ".admin-editor button", "Сохранить напоминание");
                await Wait(() => Task.FromResult(reminders.GetSnapshot().Reminders.Any(r => r.Title == "Synthetic edited reminder")), "React reminder edit persisted");
                reminders.Tick(SmokeNow, TimeSpan.Zero);
                await ClickText(web, ".admin-actions button", "Обновить напоминания");
                await ClickAria(web, "Отложить: Synthetic edited reminder");
                await Wait(() => Task.FromResult(reminders.GetSnapshot().Occurrences.Single().SnoozedUntilUtc == SmokeNow.AddMinutes(10)), "React snooze persisted UTC deadline");
                await ClickAria(web, "Скрыть: Synthetic edited reminder");
                await Wait(() => Task.FromResult(reminders.GetSnapshot().Occurrences.Single().Acknowledgment == ReminderAcknowledgment.Dismissed), "React dismiss is not completion");
                await ClickAria(web, "Выполнено: Synthetic edited reminder");
                await Wait(() => Task.FromResult(reminders.GetSnapshot().Occurrences.Single().Acknowledgment == ReminderAcknowledgment.Completed), "React explicit acknowledgment persisted");
                await ClickText(web, ".nav-item", "Настройки");
                await Wait(async () => await Script<bool>(web, "!!document.querySelector('input[name=ownerDisplayName]')"), "Settings loaded from native service");
                await SetInput(web, "input[name=ownerDisplayName]", "Synthetic WebView owner");
                await ClickText(web, ".admin-editor button", "Сохранить настройки");
                await Wait(() => Task.FromResult(host.Services.GetRequiredService<UserSettingsService>().GetSnapshot().OwnerDisplayName == "Synthetic WebView owner"), "React settings saved to canonical JSON store");
                Check(JsonSerializer.Serialize(reminders.GetSnapshot().Reminders.Single(r => r.Id == "synthetic-untouched"), ReminderDataCodec.JsonOptions) == untouched, "Unrelated reminder unchanged by React workflows");
                manager.Dispose(); host.Dispose(); host = SmokeHost(isolated);
                reminders = host.Services.GetRequiredService<IReminderCommands>(); reminders.Tick(SmokeNow, TimeSpan.Zero);
                Check(reminders.GetSnapshot().Occurrences.Single().Acknowledgment == ReminderAcknowledgment.Completed, "Acknowledgment survives restart without duplicates");
                Check(host.Services.GetRequiredService<UserSettingsService>().GetSnapshot().OwnerDisplayName == "Synthetic WebView owner", "Settings survive host restart");
                host.Dispose(); host = null;
                await VerifyInFreshProcess(isolated, "completed-workflow");
                Console.WriteLine("PASS: actual WebView2/native bridge/SQLite CRUD, reminder/settings JSON, host restart and admin window lifecycle.");
                Console.WriteLine("Synthetic diagnostics: " + output);
                exit = 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex.Message); }
            finally
            {
                manager?.Dispose(); host?.Dispose();
                app.Shutdown(exit);
            }
        };
        return app.Run();
    }
    private static void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); Console.WriteLine("PASS: " + label); }
    private static readonly DateTimeOffset SmokeNow = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    internal sealed class SmokeClock : TimeProvider { public DateTimeOffset Now = SmokeNow; public override DateTimeOffset GetUtcNow() => Now; }
    private static IHost SmokeHost(string isolated)
    {
        var autostart = new SmokeAutostart();
        string settingsPath = Path.Combine(isolated, "user-settings.json");
        var settings = new UserSettingsService(new UserSettingsStore(settingsPath, () => new UserSettings { TimeZoneId = "UTC" }), autostart);
        if (!File.Exists(settingsPath)) Check(settings.Save(settings.GetSnapshot()).Success, "Isolated UTC settings profile");
        return DesktopComposition.CreateHost(userDataDirectory: isolated, autostart: autostart, clock: new SmokeClock(), userSettings: settings);
    }
    private static async Task VerifyInFreshProcess(string profile, string stage)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet") start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--verify-profile"); start.ArgumentList.Add(profile); start.ArgumentList.Add(stage);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); } catch { process.Kill(true); throw; }
        string detail = await output + await error;
        if (process.ExitCode != 0) throw new InvalidOperationException("Fresh-process verification failed: " + detail);
        Console.Write(detail); Check(true, "Fresh process persisted profile: " + stage);
    }
    private static int VerifyProfile(string profile, string stage)
    {
        try
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "ChihDesktop.slnx"))) root = root.Parent;
            string allowed = Path.Combine(root?.FullName ?? throw new InvalidOperationException(), "artifacts", "phase4-admin-fix") + Path.DirectorySeparatorChar;
            profile = Path.GetFullPath(profile);
            if (!profile.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(profile).StartsWith("webview-", StringComparison.Ordinal)) throw new InvalidOperationException("Only isolated smoke profiles are permitted.");
            foreach (string file in new[] { "chih.db", "reminders.json", "user-settings.json" })
                if (!File.Exists(Path.Combine(profile, file))) throw new InvalidOperationException("Isolated fixture missing: " + file);
            using var host = SmokeHost(profile);
            var tasks = host.Services.GetRequiredService<IPlannerService>().GetTasksAsync().GetAwaiter().GetResult();
            Check(tasks.Success, "Fresh process SQLite query");
            if (stage == "task-created")
            {
                Check(tasks.Value?.Single().Title == "Synthetic WebView task", "React-created task persisted across process restart");
                Check(tasks.Value?.Single().RemindAtUtc == DateTimeOffset.Parse("2050-12-01T12:00:00Z") && tasks.Value.Single().DueDate is null,
                    "Task reminder instant persists across process restart without a due date");
            }
            else if (stage == "completed-workflow")
            {
                Check(tasks.Value?.Count == 0, "Confirmed React deletion persisted across process restart");
                var snapshot = host.Services.GetRequiredService<IReminderCommands>().GetSnapshot();
                Check(snapshot.Reminders.Any(r => r.Title == "Synthetic edited reminder" && r.Enabled), "React reminder edit persisted across process restart");
                Check(snapshot.Occurrences.Single().Acknowledgment == ReminderAcknowledgment.Completed, "Explicit React acknowledgment persisted across process restart");
                Check(host.Services.GetRequiredService<UserSettingsService>().GetSnapshot().OwnerDisplayName == "Synthetic WebView owner", "React settings persisted across process restart");
            }
            else throw new InvalidOperationException("Unknown verification stage.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }
    private sealed class SmokeAutostart : IAutostartRegistration
    {
        public string? Read() => throw new InvalidOperationException("Smoke tests cannot access autostart.");
        public void Write(string? command) => throw new InvalidOperationException("Smoke tests cannot access autostart.");
        public string Command(bool petOnly) => throw new InvalidOperationException("Smoke tests cannot access autostart.");
    }
    private static async Task Wait(Func<Task<bool>> predicate, string label)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(30)) { if (await predicate()) { Console.WriteLine("PASS: " + label); return; } await Task.Delay(100); }
        throw new TimeoutException(label);
    }
    private static async Task<T> Script<T>(WebView2 web, string expression) => JsonSerializer.Deserialize<T>(await web.ExecuteScriptAsync(expression))!;
    private static async Task ClickText(WebView2 web, string selector, string text)
    {
        string script = $"(()=>{{const b=[...document.querySelectorAll({JsonSerializer.Serialize(selector)})].find(b=>b.textContent.trim().endsWith({JsonSerializer.Serialize(text)}));if(!b||b.disabled)return false;b.click();return true;}})()";
        await Wait(() => Script<bool>(web, script), "Click " + text);
    }
    private static async Task ClickAria(WebView2 web, string text)
    {
        string script = $"(()=>{{const b=[...document.querySelectorAll('button')].find(b=>b.getAttribute('aria-label')==={JsonSerializer.Serialize(text)});if(!b||b.disabled)return false;b.click();return true;}})()";
        await Wait(() => Script<bool>(web, script), "Click " + text);
    }
    private static Task<string> SetTitle(WebView2 web, string title) => web.ExecuteScriptAsync($"(()=>{{const input=document.querySelector('[role=dialog] input[name=title]');Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(input,{JsonSerializer.Serialize(title)});input.dispatchEvent(new Event('input',{{bubbles:true}}));}})()");
    private static Task<string> SetInput(WebView2 web, string selector, string value) => web.ExecuteScriptAsync($"(()=>{{const input=document.querySelector({JsonSerializer.Serialize(selector)});Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(input,{JsonSerializer.Serialize(value)});input.dispatchEvent(new Event('input',{{bubbles:true}}));}})()");
}
