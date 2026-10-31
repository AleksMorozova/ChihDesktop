using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace ChihDesktop.Tests;

public sealed class R1PrivacyTests
{
    private static string Workspace => SpriteOutputPolicy.FindWorkspace(AppContext.BaseDirectory);

    [Fact]
    public void Clean_distributable_defaults_have_no_personal_records_or_owner()
    {
        var reminders = ReminderData.CreateDefaults(); var settings = new UserSettings();
        Assert.Empty(reminders.Reminders); Assert.Empty(reminders.Occurrences);
        Assert.Equal("", settings.OwnerDisplayName); Assert.False(settings.LaunchAtSignIn); Assert.False(settings.OnboardingCompleted);
        Assert.Equal(TimeZoneInfo.Local.Id, settings.TimeZoneId); Assert.Equal("ru", settings.Language);
        using var defaults = JsonDocument.Parse(File.ReadAllText(Path.Combine(Workspace, "appsettings.json")));
        Assert.Equal(new[] { "AlwaysOnTop", "AnimationFps", "CatSize", "WalkingSpeed" }, defaults.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray());
    }

    [Fact]
    public void Project_embeds_only_production_frames_and_excludes_private_source_roots()
    {
        var project = XDocument.Load(Path.Combine(Workspace, "ChihDesktop.csproj"));
        Assert.Equal("0.3.0", project.Descendants("Version").Single().Value);
        Assert.Equal("Assets/Sprites/*.png", project.Descendants("Resource").Single().Attribute("Include")!.Value);
        var excludes = project.Descendants("DefaultItemExcludes").Single().Value;
        Assert.Contains("references/**", excludes); Assert.Contains("Assets/Backups/**", excludes); Assert.Contains("artifacts/**", excludes);
        var defaults = project.Descendants("None").Single(e => (string?)e.Attribute("Update") == "appsettings.json");
        Assert.Equal("Never", defaults.Attribute("CopyToPublishDirectory")!.Value);
        Assert.Equal(15, Directory.GetFiles(Path.Combine(Workspace, "Assets", "Sprites"), "*.png").Length);
    }

    [Theory]
    [InlineData("references/example.jpg")]
    [InlineData("Assets/Backups/example.png")]
    [InlineData("reminders.json")]
    [InlineData("user-settings.json")]
    [InlineData("settings.json")]
    [InlineData("appsettings.local.json")]
    [InlineData("chih.db")]
    [InlineData("chih.db-wal")]
    [InlineData("chih.db-shm")]
    [InlineData("chih.db-journal")]
    [InlineData("backup.sqlite3-wal")]
    [InlineData("logs/application.log")]
    [InlineData(".env.local")]
    [InlineData("certificate.pfx")]
    [InlineData("certificate.p12")]
    [InlineData("private.key")]
    [InlineData(".aws/credentials")]
    [InlineData("reminders.json.pre-v03-backup")]
    public void Actual_publish_guard_rejects_private_artifacts(string path) => Assert.True(GuardPattern().IsMatch(path), path);

    [Theory]
    [InlineData("AdminUi/index.html")]
    [InlineData("AdminUi/assets/index-example.css")]
    [InlineData("ChihDesktop.dll")]
    [InlineData("ChihDesktop.runtimeconfig.json")]
    [InlineData("ChihDesktop.deps.json")]
    [InlineData("runtimes/win-x64/native/WebView2Loader.dll")]
    public void Actual_publish_guard_allows_application_binaries_and_compiled_frontend(string path) => Assert.False(GuardPattern().IsMatch(path), path);

    private static Regex GuardPattern()
    {
        var project = XDocument.Load(Path.Combine(Workspace, "ChihDesktop.csproj"));
        var guard = project.Descendants("Target").Single(e => (string?)e.Attribute("Name") == "GuardReleasePrivacy");
        Assert.Equal("ComputeFilesToPublish", guard.Attribute("AfterTargets")!.Value);
        Assert.Single(guard.Descendants("Error"));
        string condition = guard.Descendants("_PrivatePublishFile").Single().Attribute("Condition")!.Value;
        var match = Regex.Match(condition, "IsMatch\\('[^']*', '([^']*)'\\)");
        Assert.True(match.Success, "Privacy guard must inspect the actual relative publish path.");
        return new Regex(match.Groups[1].Value);
    }
}
