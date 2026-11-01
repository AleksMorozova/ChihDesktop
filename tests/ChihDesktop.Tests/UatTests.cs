using System.Windows.Media.Imaging;
using Xunit;

namespace ChihDesktop.Tests;

public sealed class UatTests
{
    [Fact]
    public void Repeated_isolated_reminder_creates_surface_persistence_failures()
    {
        using var files = new TempFiles(); string path = files.File("synthetic-reminders.json");
        var commands = new ReminderCommands(new ReminderStore(path), userSettings: TestSettings.Create());
        for (int i = 0; i < 50; i++)
        {
            var result = commands.CreateReminder(new() { Title = "Synthetic " + i, Time = new(20, 0) });
            Assert.True(result.Success, result.Error);
        }
        Assert.Equal(50, new ReminderStore(path).Load().Data!.Reminders.Count);
    }
    [Fact]
    public void Application_and_installer_share_the_approved_icon_and_dynamic_version()
    {
        string root = SpriteOutputPolicy.FindWorkspace(AppContext.BaseDirectory);
        var project = System.Xml.Linq.XDocument.Load(Path.Combine(root, "ChihDesktop.csproj"));
        Assert.Equal("Assets/Branding/chih-icon.ico", project.Descendants("ApplicationIcon").Single().Value);
        string installer = File.ReadAllText(Path.Combine(root, "installer/ChihDesktop.iss"));
        Assert.Contains("SetupIconFile=..\\Assets\\Branding\\chih-icon.ico", installer);
        Assert.Contains("IconFilename: \"{app}\\ChihDesktop.exe\"", installer);
        Assert.DoesNotContain("#define AppVersion \"0.3.0\"", installer);
        string build = File.ReadAllText(Path.Combine(root, "scripts/Build-Installer.ps1"));
        Assert.Contains("/DAppVersion=$version", build); Assert.Contains("Test-AppIcon.ps1", build);
    }
    [Fact]
    public void Approved_png_and_all_seven_ico_frames_decode()
    {
        string root = SpriteOutputPolicy.FindWorkspace(AppContext.BaseDirectory);
        using var png = File.OpenRead(Path.Combine(root, "Assets/Branding/chih-icon.png"));
        var picture = BitmapDecoder.Create(png, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        Assert.True(picture.Frames.Single().PixelWidth > 256);
        using var ico = File.OpenRead(Path.Combine(root, "Assets/Branding/chih-icon.ico"));
        var decoder = new IconBitmapDecoder(ico, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        Assert.Equal(new[] { 16, 24, 32, 48, 64, 128, 256 }, decoder.Frames.Select(f => f.PixelWidth).Order().ToArray());
        Assert.All(decoder.Frames, f => Assert.Equal(f.PixelWidth, f.PixelHeight));
    }

    [Fact]
    public void Explicit_petting_is_visible_varied_and_coalesced_without_bypassing_priority()
    {
        var now = DateTimeOffset.Parse("2026-11-01T12:00:00Z"); var options = new PersonalityOptions();
        var service = new PersonalityService(() => options, now, new Random(1));
        var phrases = new HashSet<string>();
        for (int i = 0; i < DialogueCatalog.Phrases[DialogueCategory.Petting].Length; i++)
        {
            var at = now.AddSeconds(i * 4); service.Pet(at);
            Assert.True(phrases.Add(service.PetReply(at, true, requested: true)!));
            Assert.Null(service.PetReply(at.AddMilliseconds(20), true, requested: true));
        }
        Assert.Null(service.PetReply(now.AddHours(1), false, requested: true)); // Reminder/fullscreen/quiet gate.
        options.PersonalityEnabled = false;
        Assert.Null(service.PetReply(now.AddHours(2), true, requested: true));
    }

    [Theory]
    [InlineData(CatState.Idle)]
    [InlineData(CatState.Sleeping)]
    public void Explicit_reaction_and_sleep_wake_are_safe_while_paused(CatState initial)
    {
        var behavior = new CatBehaviorService { IsPaused = true };
        if (initial == CatState.Sleeping) behavior.Sleep();
        behavior.React(); Assert.Equal(CatState.Reacting, behavior.State);
        behavior.Update(100); Assert.Equal(CatState.Reacting, behavior.State);
        behavior.Sleep(); Assert.Equal(CatState.Sleeping, behavior.State);
        behavior.WakeUp(); Assert.Equal(CatState.Stretching, behavior.State);
        Assert.True(behavior.IsPaused);
        behavior.IsPaused = false; behavior.Update(100); Assert.NotEqual(CatState.Stretching, behavior.State);
    }
}
