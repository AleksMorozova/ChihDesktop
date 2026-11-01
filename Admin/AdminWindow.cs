using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChihDesktop;

public sealed class AdminWindow : Window
{
    private readonly AdminBridge bridge;
    private readonly WebView2 web = new() { Visibility = Visibility.Collapsed };
    private readonly Grid layout = new();
    private readonly TextBlock status = new() { Text = "Чих открывает планировщик…", Margin = new(32), TextWrapping = TextWrapping.Wrap };
    private bool closed, initializing;
    private readonly Action? openNativeSettings;
    private readonly AdminDiagnostics diagnostics;
    public AdminWindow(IPlannerService planner, Action? openSettings = null, AdminApplicationApi? application = null, AdminDiagnostics? diagnostics = null)
    {
        this.diagnostics = diagnostics ?? AdminDiagnostics.Disabled;
        this.diagnostics.Write("admin.construct");
        bridge = new(planner, openSettings, application);
        openNativeSettings = openSettings;
        AppBranding.Apply(this);
        Title = "Chih · Планировщик";
        Width = 1180; Height = 790; MinWidth = 760; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = System.Windows.Media.Brushes.White;
        layout.Children.Add(status); layout.Children.Add(web); Content = layout;
        Loaded += Initialize;
        Activated += (_, _) => this.diagnostics.Window("admin.activated", this);
        IsVisibleChanged += (_, _) => this.diagnostics.Window("admin.visibility", this);
        StateChanged += (_, _) => this.diagnostics.Window("admin.state", this);
        Closed += (_, _) =>
        {
            closed = true; bridge.Dispose();
            this.diagnostics.Write("admin.closed");
            if (web.CoreWebView2 is { } core)
            {
                core.WebMessageReceived -= OnMessage;
                core.NavigationStarting -= OnNavigation;
                core.FrameNavigationStarting -= OnFrameNavigation;
                core.NewWindowRequested -= OnPopup;
                core.WebResourceRequested -= OnResource;
                core.PermissionRequested -= OnPermission;
                core.DownloadStarting -= OnDownload;
                core.WebResourceResponseReceived -= OnResourceResponse;
                core.ProcessFailed -= OnProcessFailed;
            }
            web.Dispose();
        };
    }

    private async void Initialize(object sender, RoutedEventArgs e)
    {
        if (initializing || closed) return;
        initializing = true;
        diagnostics.Window("admin.loaded", this);
        try
        {
            string files = Path.Combine(AppContext.BaseDirectory, "AdminUi");
            bool exists = File.Exists(Path.Combine(files, "index.html"));
            diagnostics.Write("admin.assets", new { directory = files, indexExists = exists });
            if (!exists) throw new FileNotFoundException();
            string runtimeVersion = CoreWebView2Environment.GetAvailableBrowserVersionString();
            diagnostics.Write("webview.runtime", new { runtimeVersion });
            string profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChihDesktop", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profile);
            diagnostics.Write("webview.environment.created");
            if (closed) return;
            await web.EnsureCoreWebView2Async(environment);
            diagnostics.Write("webview.initialized");
            if (closed) return;
            var core = web.CoreWebView2;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDefaultScriptDialogsEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsBuiltInErrorPageEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Profile.PreferredTrackingPreventionLevel = CoreWebView2TrackingPreventionLevel.Strict;
            core.SetVirtualHostNameToFolderMapping(AdminNavigationPolicy.Host, files, CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting += OnNavigation;
            core.FrameNavigationStarting += OnFrameNavigation;
            core.NewWindowRequested += OnPopup;
            core.WebMessageReceived += OnMessage;
            core.PermissionRequested += OnPermission;
            core.DownloadStarting += OnDownload;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += OnResource;
            core.WebResourceResponseReceived += OnResourceResponse;
            core.ProcessFailed += OnProcessFailed;
            core.NavigationCompleted += (_, args) =>
            {
                diagnostics.Write("webview.navigation.complete", new { args.IsSuccess, status = args.WebErrorStatus.ToString(), args.HttpStatusCode });
                if (closed) return;
                if (args.IsSuccess) web.Visibility = Visibility.Visible;
                else ShowError("Не удалось загрузить интерфейс. Закройте окно и откройте его снова.");
            };
            // The application version may stay the same during local rebuilds. Bust only the
            // entry-page cache when its production asset references change; hashed JS/CSS stay local.
            string build = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(files, "index.html"))));
            core.Navigate(AdminNavigationPolicy.StartUrl + "?build=" + build);
            diagnostics.Write("webview.navigate.requested", new { build });
        }
        catch (WebView2RuntimeNotFoundException ex)
        {
            diagnostics.Write("webview.runtime.missing", error: ex);
            if (!closed) ShowError("Microsoft Edge WebView2 Runtime не установлен. Чих, нативные настройки и напоминания продолжают работать. Для планировщика установите официальный Evergreen WebView2 Runtime. Онлайн bootstrapper требует интернет; для офлайн-установки используйте Standalone Installer x64. Затем заново откройте планировщик.");
        }
        catch (Exception ex)
        {
            diagnostics.Write("webview.initialize.failed", error: ex);
            if (!closed) ShowError("Не удалось открыть планировщик. Проверьте наличие Microsoft Edge WebView2 Runtime и папки AdminUi рядом с приложением. Закройте окно и повторите попытку.");
        }
    }

    internal void ShowError(string message)
    {
        diagnostics.Write("admin.error.visible");
        web.Visibility = Visibility.Collapsed;
        status.Text = message;
        foreach (var previous in layout.Children.OfType<Button>().ToArray()) layout.Children.Remove(previous);
        var help = new Button { Content = "Инструкция по WebView2 ↗", HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new(32) };
        help.Click += (_, _) => OpenExternal(AdminNavigationPolicy.RuntimeHelp);
        layout.Children.Add(help);
        if (openNativeSettings is not null)
        {
            var native = new Button { Content = "Открыть нативные настройки", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new(32) };
            native.Click += (_, _) => openNativeSettings(); layout.Children.Add(native);
        }
    }
    private void OnNavigation(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        e.Cancel = !AdminNavigationPolicy.IsLocal(e.Uri);
        diagnostics.Write("webview.navigation.start", new { blocked = e.Cancel });
    }
    private void OnFrameNavigation(object? sender, CoreWebView2NavigationStartingEventArgs e) => e.Cancel = true;
    private void OnPopup(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (e.IsUserInitiated && AdminNavigationPolicy.IsApprovedExternal(e.Uri)) OpenExternal(e.Uri);
    }
    private static void OpenExternal(string uri)
    {
        if (!AdminNavigationPolicy.IsApprovedExternal(uri)) return;
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch (Exception) { MessageBox.Show("Не удалось открыть браузер. Проверьте браузер по умолчанию.", "ChihDesktop"); }
    }
    private void OnResource(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (!AdminNavigationPolicy.IsLocal(e.Request.Uri))
            e.Response = web.CoreWebView2.Environment.CreateWebResourceResponse(null, 403, "Blocked", "Content-Type: text/plain");
    }
    private void OnPermission(object? sender, CoreWebView2PermissionRequestedEventArgs e) => e.State = CoreWebView2PermissionState.Deny;
    private void OnResourceResponse(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        if (e.Response.StatusCode >= 400 && AdminNavigationPolicy.IsLocal(e.Request.Uri))
            diagnostics.Write("webview.resource.failed", new { status = e.Response.StatusCode, asset = new Uri(e.Request.Uri).AbsolutePath });
    }
    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        diagnostics.Write("webview.process.failed", new { kind = e.ProcessFailedKind.ToString() });
        if (!closed) ShowError("Процесс WebView2 завершился. Закройте планировщик и откройте его снова. Диагностика: " + diagnostics.LogPath);
    }
    private void OnDownload(object? sender, CoreWebView2DownloadStartingEventArgs e) => e.Cancel = true;
    private async void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (closed || !AdminNavigationPolicy.IsLocal(e.Source)) return;
        try
        {
            var response = await bridge.HandleAsync(e.WebMessageAsJson);
            diagnostics.Write("bridge.reply", new { response.Success, operation = response.Success ? response.Operation : null, errorCode = response.Error?.Code });
            if (!closed && AdminNavigationPolicy.IsLocal(web.CoreWebView2.Source))
                web.CoreWebView2.PostWebMessageAsJson(AdminBridge.Serialize(response));
        }
        catch (Exception) { /* A disposed or failed renderer cannot receive a reply. No payload logging. */ }
    }
}

public sealed class AdminWindowManager(IPlannerService planner, UserSettingsWindowManager settings, AdminApplicationApi application, Func<AdminWindow>? createWindow = null, AdminDiagnostics? diagnostics = null) : IDisposable
{
    private AdminWindow? window;
    private readonly AdminDiagnostics log = diagnostics ?? AdminDiagnostics.Disabled;
    internal static void AfterMenuClosed(ContextMenu menu, bool menuSessionOpen, Action open)
    {
        // IsOpen becomes false before the closing animation completes. Dispatcher
        // priority alone does not establish that the menu's Closed event has fired.
        if (!menuSessionOpen && !menu.IsOpen) { menu.Dispatcher.BeginInvoke(open); return; }
        RoutedEventHandler? closed = null;
        closed = (_, _) => { menu.Closed -= closed; menu.Dispatcher.BeginInvoke(open); };
        menu.Closed += closed;
    }
    public void Show()
    {
        log.Write("admin.open.request", new { existing = window is not null });
        try
        {
            if (window is null)
            {
                window = createWindow?.Invoke() ?? new(planner, () => settings.Show(), application, log);
                log.Window("admin.created", window);
                window.Closed += (_, _) => window = null;
            }
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            if (!window.IsVisible) window.Show();
            new WindowPositionService(window).FitToWorkArea();
            log.Window("admin.show.returned", window);
            bool activated = window.Activate();
            log.Write("admin.activate.returned", new { activated });
            log.Window("admin.visible", window);
        }
        catch (Exception ex)
        {
            log.Write("admin.open.failed", error: ex);
            MessageBox.Show("Не удалось открыть планировщик Чиха. Диагностика: " + (log.LogPath ?? "недоступна"), "ChihDesktop", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    public void Dispose() { window?.Close(); window = null; }
}
