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
    private readonly WebView2 web = new();
    private readonly Grid layout = new();
    private readonly TextBlock status = new() { Text = "Чих открывает планировщик…", Margin = new(32), TextWrapping = TextWrapping.Wrap };
    private bool closed, initializing;
    private readonly Action? openNativeSettings;
    public AdminWindow(IPlannerService planner, Action? openSettings = null)
    {
        bridge = new(planner, openSettings);
        openNativeSettings = openSettings;
        Title = "Chih · Планировщик";
        Width = 1180; Height = 790; MinWidth = 760; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = System.Windows.Media.Brushes.White;
        layout.Children.Add(status); layout.Children.Add(web); Content = layout;
        Loaded += Initialize;
        Closed += (_, _) =>
        {
            closed = true; bridge.Dispose();
            if (web.CoreWebView2 is { } core)
            {
                core.WebMessageReceived -= OnMessage;
                core.NavigationStarting -= OnNavigation;
                core.FrameNavigationStarting -= OnFrameNavigation;
                core.NewWindowRequested -= OnPopup;
                core.WebResourceRequested -= OnResource;
                core.PermissionRequested -= OnPermission;
                core.DownloadStarting -= OnDownload;
            }
            web.Dispose();
        };
    }

    private async void Initialize(object sender, RoutedEventArgs e)
    {
        if (initializing || closed) return;
        initializing = true;
        try
        {
            string files = Path.Combine(AppContext.BaseDirectory, "AdminUi");
            if (!File.Exists(Path.Combine(files, "index.html"))) throw new FileNotFoundException();
            string profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChihDesktop", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profile);
            if (closed) return;
            await web.EnsureCoreWebView2Async(environment);
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
            core.NavigationCompleted += (_, args) =>
            {
                if (!closed && !args.IsSuccess) ShowError("Не удалось загрузить интерфейс. Закройте окно и откройте его снова.");
            };
            core.Navigate(AdminNavigationPolicy.StartUrl);
        }
        catch (Exception)
        {
            if (!closed) ShowError("Не удалось открыть планировщик. Проверьте наличие Microsoft Edge WebView2 Runtime и папки AdminUi рядом с приложением. Закройте окно и повторите попытку.");
        }
    }

    private void ShowError(string message)
    {
        web.Visibility = Visibility.Collapsed;
        status.Text = message;
        var help = new Button { Content = "Инструкция по WebView2 ↗", HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new(32) };
        help.Click += (_, _) => OpenExternal(AdminNavigationPolicy.RuntimeHelp);
        layout.Children.Add(help);
        if (openNativeSettings is not null)
        {
            var native = new Button { Content = "Открыть нативные настройки", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new(32) };
            native.Click += (_, _) => openNativeSettings(); layout.Children.Add(native);
        }
    }
    private void OnNavigation(object? sender, CoreWebView2NavigationStartingEventArgs e) => e.Cancel = !AdminNavigationPolicy.IsLocal(e.Uri);
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
    private void OnDownload(object? sender, CoreWebView2DownloadStartingEventArgs e) => e.Cancel = true;
    private async void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (closed || !AdminNavigationPolicy.IsLocal(e.Source)) return;
        try
        {
            var response = await bridge.HandleAsync(e.WebMessageAsJson);
            if (!closed && AdminNavigationPolicy.IsLocal(web.CoreWebView2.Source))
                web.CoreWebView2.PostWebMessageAsJson(AdminBridge.Serialize(response));
        }
        catch (Exception) { /* A disposed or failed renderer cannot receive a reply. No payload logging. */ }
    }
}

public sealed class AdminWindowManager(IPlannerService planner, UserSettingsWindowManager settings) : IDisposable
{
    private AdminWindow? window;
    public void Show()
    {
        if (window is null)
        {
            window = new(planner, () => settings.Show());
            window.Closed += (_, _) => window = null;
            window.Show();
        }
        else
        {
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();
        }
    }
    public void Dispose() { window?.Close(); window = null; }
}
