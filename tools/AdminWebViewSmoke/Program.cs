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
    private static int Main()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "ChihDesktop.slnx"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Run the smoke test from the workspace.");
        string output = Path.Combine(root.FullName, "artifacts", "phase4-admin-fix");
        string isolated = Path.Combine(output, "webview-" + Guid.NewGuid().ToString("N"));
        string profile = Path.Combine(isolated, "profile");
        Directory.CreateDirectory(isolated);
        // Official WebView2 override, process-local; checked against the actual environment below.
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", profile);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            IHost? host = null; AdminWindow? window = null;
            int exit = 1;
            try
            {
                host = DesktopComposition.CreateHost(plannerDatabase: new PlannerDatabaseOptions(Path.Combine(isolated, "test.db")),
                    userSettings: new UserSettingsService(new UserSettingsStore(Path.Combine(isolated, "user-settings.json"), () => new UserSettings { TimeZoneId = "Europe/Kyiv" }), new SmokeAutostart()));
                var planner = host.Services.GetRequiredService<IPlannerService>();
                window = new AdminWindow(planner) { ShowInTaskbar = false, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -2000, Top = -2000 };
                var grid = (Grid)window.Content;
                var web = grid.Children.OfType<WebView2>().Single();
                window.Show();
                await Wait(async () => web.CoreWebView2 is not null && await Script<bool>(web, "!!document.querySelector('.task-panel') && !document.querySelector('.loading')"), "WebView2/frontend initialization");
                Check(AdminNavigationPolicy.IsLocal(web.CoreWebView2.Source), "Virtual-host origin");
                Check(Path.GetFullPath(web.CoreWebView2.Environment.UserDataFolder) == Path.GetFullPath(profile), "Isolated WebView2 profile");
                Check(await Script<bool>(web, "!!window.chrome?.webview"), "Native bridge exists");
                Check(await Script<bool>(web, "!document.querySelector('.runtime-banner') && !document.querySelector('[role=alert]')"), "Real native mode without mock data/errors");
                Check(await Script<string>(web, "getComputedStyle(document.querySelector('.app-shell')).display") == "flex", "Production layout CSS");
                Check((await Script<string>(web, "getComputedStyle(document.documentElement).fontFamily")).Contains("Segoe UI"), "Production fonts");

                await ClickText(web, ".nav-item", "Задачи");
                await Wait(async () => await Script<bool>(web, "!!document.querySelector('.filters') && !document.querySelector('.loading')"), "Tasks view");
                await web.ExecuteScriptAsync("document.querySelector('button.primary').click()");
                await Wait(async () => await Script<bool>(web, "!!document.querySelector('[role=dialog] input[name=title]')"), "Create dialog");
                await SetTitle(web, "Synthetic WebView task");
                await web.ExecuteScriptAsync("document.querySelector('[role=dialog] form').requestSubmit()");
                await Wait(async () => (await planner.GetTasksAsync()).Value?.Any(t => t.Title == "Synthetic WebView task") == true, "Create through JS/native bridge/SQLite");
                await Wait(async () => await Script<bool>(web, "!document.querySelector('[role=dialog]') && document.body.textContent.includes('Synthetic WebView task')"), "Created task displayed");
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
                Console.WriteLine("PASS: actual WebView2/production assets/native bridge/SQLite CRUD. Runtime " + web.CoreWebView2.Environment.BrowserVersionString);
                Console.WriteLine("Synthetic diagnostics: " + output);
                exit = 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex.Message); }
            finally
            {
                window?.Close(); host?.Dispose();
                app.Shutdown(exit);
            }
        };
        return app.Run();
    }
    private static void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); Console.WriteLine("PASS: " + label); }
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
}
