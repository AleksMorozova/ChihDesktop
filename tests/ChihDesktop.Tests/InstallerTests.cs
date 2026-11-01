using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Wpf;
using Xunit;

namespace ChihDesktop.Tests;

public sealed class InstallerTests
{
    private static string Workspace => SpriteOutputPolicy.FindWorkspace(AppContext.BaseDirectory);
    private static string Installer => File.ReadAllText(Path.Combine(Workspace, "installer", "ChihDesktop.iss"));

    [Theory]
    [InlineData("AppId", "{{DBB073A1-919D-4B98-BF22-6E77BDF22C23}")]
    [InlineData("AppVersion", "{#AppVersion}")]
    [InlineData("PrivilegesRequired", "lowest")]
    [InlineData("DefaultDirName", "{localappdata}\\Programs\\ChihDesktop")]
    [InlineData("AppMutex", "Local\\ChihDesktop.DesktopPet")]
    [InlineData("UsePreviousAppDir", "yes")]
    [InlineData("UsePreviousTasks", "yes")]
    [InlineData("CloseApplications", "no")]
    [InlineData("RestartApplications", "no")]
    public void Installer_identity_privilege_and_upgrade_contract_is_stable(string name, string expected)
    {
        Assert.Equal(expected, Regex.Match(Installer, $@"(?m)^{name}=(.+)\r?$" ).Groups[1].Value.Trim());
    }

    [Fact]
    public void Installer_preserves_data_and_never_enables_startup_implicitly()
    {
        Assert.DoesNotMatch(@"(?im)^\[(Registry|InstallDelete|UninstallDelete)\]", Installer);
        Assert.Contains("--remove-autostart", Installer);
        Assert.Contains("RunOnceId: \"RemoveChihDesktopAutostart\"", Installer);
        Assert.Contains("WorkingDir: \"{app}\"", Installer);
        Assert.Contains("Flags: unchecked; Check: NeedsWebView2", Installer);
        Assert.Contains("GetSHA256OfFile(Bootstrapper)", Installer);
        Assert.Contains("HasRuntimeAt(HKLM32)", Installer);
        Assert.Contains("HasRuntimeAt(HKCU32)", Installer);
        Assert.Contains("'/silent /install'", Installer);
    }

    [Fact]
    public void Publish_profile_is_untrimmed_self_contained_x64_with_bundled_host()
    {
        var profile = XDocument.Load(Path.Combine(Workspace, "Properties", "PublishProfiles", "WinX64.pubxml"));
        Assert.Equal("win-x64", profile.Descendants("RuntimeIdentifier").Single().Value);
        foreach (string name in new[] { "SelfContained", "UseAppHost" }) Assert.Equal("true", profile.Descendants(name).Single().Value);
        foreach (string name in new[] { "PublishTrimmed", "PublishSingleFile", "PublishReadyToRun" }) Assert.Equal("false", profile.Descendants(name).Single().Value);
    }

    [Theory]
    [InlineData("reminders.json")]
    [InlineData("user-settings.json.bak")]
    [InlineData("chih.db-wal")]
    [InlineData("references/example.jpg")]
    [InlineData(".env.local")]
    [InlineData("logs/example.log")]
    [InlineData("AdminUi/assets/source.js.map")]
    [InlineData("node_modules/example.js")]
    public async Task Payload_validator_rejects_private_files_before_loading_any_application(string relative)
    {
        // Only isolated synthetic artifacts. No developer LocalAppData or registry access.
        string folder = Path.Combine(Workspace, "artifacts", "R21Tests", Guid.NewGuid().ToString("N"));
        string file = Path.Combine(folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "synthetic canary");
        var result = await RunScript("Test-Publish.ps1", "-PublishDirectory", folder);
        Assert.NotEqual(0, result.Exit);
        Assert.Contains("Private/development file", result.Output);
        Assert.Equal("synthetic canary", File.ReadAllText(file));
    }

    [Fact]
    public async Task Payload_validator_reports_incomplete_output_and_rejects_non_artifact_paths()
    {
        string empty = Path.Combine(Workspace, "artifacts", "R21Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        var missing = await RunScript("Test-Publish.ps1", "-PublishDirectory", empty);
        Assert.NotEqual(0, missing.Exit); Assert.Contains("Missing publish dependency", missing.Output);
        var unsafePath = await RunScript("Test-Publish.ps1", "-PublishDirectory", Workspace);
        Assert.NotEqual(0, unsafePath.Exit); Assert.Contains("must be inside", unsafePath.Output);
    }

    [Fact]
    public void Native_error_panel_is_visible_and_settings_remain_accessible_without_a_renderer()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var files = new TempFiles();
                using var host = DesktopComposition.CreateHost(userDataDirectory: files.File("profile"), autostart: new MemoryAutostart());
                int settingsOpened = 0;
                var window = new AdminWindow(host.Services.GetRequiredService<IPlannerService>(), () => settingsOpened++);
                try
                {
                    window.ShowError("Synthetic missing WebView2 Runtime");
                    window.ShowError("Synthetic retry failure");
                    var grid = Assert.IsType<Grid>(window.Content);
                    Assert.Equal(Visibility.Collapsed, grid.Children.OfType<WebView2>().Single().Visibility);
                    Assert.Equal("Synthetic retry failure", grid.Children.OfType<TextBlock>().Single().Text);
                    Assert.Equal(2, grid.Children.OfType<Button>().Count());
                    grid.Children.OfType<Button>().Single(b => b.Content.ToString() == "Открыть нативные настройки").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Equal(1, settingsOpened);
                }
                finally { window.Close(); }
            }
            catch (Exception ex) { error = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    private static async Task<(int Exit, string Output)> RunScript(string script, params string[] arguments)
    {
        var start = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (string item in new[] { "-NoProfile", "-File", Path.Combine(Workspace, "scripts", script) }.Concat(arguments)) start.ArgumentList.Add(item);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(true); throw; }
        return (process.ExitCode, await output + await error);
    }
}
