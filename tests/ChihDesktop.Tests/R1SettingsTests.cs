using System.Text.Json;
using ChihDesktop.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ChihDesktop.Tests;

public sealed class R1SettingsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Unreadable_or_future_reminder_profile_is_not_mistaken_for_a_new_install()
    {
        using var files = new TempFiles(); string path = files.File("reminders.json");
        const string future = "{\"Version\":99,\"FutureField\":true}";
        File.WriteAllText(path, future);
        var store = new MemorySettings(); var settings = new UserSettingsService(store, new MemoryAutostart());
        var commands = new ReminderCommands(new ReminderStore(path), userSettings: settings);
        Assert.Equal(ReminderLoadStatus.BlockedUnsupported, commands.LoadStatus);
        Assert.False(settings.ShouldOnboard); Assert.False(settings.GetSnapshot().OnboardingCompleted);
        Assert.False(settings.SaveOnboardingDraftIfNeeded().Success); Assert.Equal(0, store.Saves);
        Assert.Equal(future, File.ReadAllText(path));
        Assert.False(commands.CreateReminder(new() { Title = "Synthetic" }).Success);
    }

    [Fact]
    public async Task Clean_profile_is_empty_isolated_and_onboarding_stays_incomplete_after_restart()
    {
        using var files = new TempFiles(); var registry = new MemoryAutostart();
        using (var host = DesktopComposition.CreateHost(userDataDirectory: files.Directory, autostart: registry))
        {
            await host.StartAsync(TestContext.Current.CancellationToken);
            Assert.Empty(System.IO.Directory.GetFiles(files.Directory));
            var reminders = host.Services.GetRequiredService<IReminderCommands>();
            var settings = host.Services.GetRequiredService<UserSettingsService>();
            Assert.Empty(reminders.GetSnapshot().Reminders);
            Assert.Empty(reminders.GetSnapshot().Occurrences);
            Assert.Equal(TimeZoneInfo.Local.Id, settings.GetSnapshot().TimeZoneId);
            Assert.True(settings.ShouldOnboard);
            Assert.True(settings.SaveOnboardingDraftIfNeeded().Success);
            Assert.False(settings.GetSnapshot().OnboardingCompleted);
            Assert.True((await host.Services.GetRequiredService<IPlannerDatabaseInitializer>().InitializeAsync(TestContext.Current.CancellationToken)).Success);
            Assert.Single(host.Services.GetServices<IPlannerService>());
            Assert.Empty(Value(await host.Services.GetRequiredService<IPlannerService>().GetTasksAsync(cancellationToken: TestContext.Current.CancellationToken)));
            Assert.Equal(Path.Combine(files.Directory, "reminders.json"), reminders.ConfigurationPath);
            Assert.False(File.Exists(reminders.ConfigurationPath));
            await host.StopAsync(TestContext.Current.CancellationToken);
        }
        using var restarted = DesktopComposition.CreateHost(userDataDirectory: files.Directory, autostart: registry);
        var current = restarted.Services.GetRequiredService<UserSettingsService>();
        Assert.True(current.ShouldOnboard);
        Assert.False(current.GetSnapshot().LaunchAtSignIn);
        Assert.True(current.GetSnapshot().StartPetOnly);
        Assert.Equal("", current.GetSnapshot().OwnerDisplayName);
        Assert.Equal(0, registry.Writes);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Existing_records_and_preferences_are_retained_byte_for_byte_until_an_explicit_write(int version)
    {
        using var files = new TempFiles();
        var original = SyntheticReminderFixtures.Schedule(); original.Version = version;
        original.Personality.MinimumCommentIntervalMinutes = 47; original.Personality.PersonalityEnabled = false;
        original.Reminders[1].LastFiredLocalDate = new(2026, 10, 31);
        original.Occurrences.Add(new() { Id = "synthetic-pending", ReminderId = original.Reminders[1].Id, Title = "Synthetic pending", LocalDate = new(2026, 10, 31), DueAtUtc = Now, SnoozedUntilUtc = Now.AddMinutes(10), NotifiedAtUtc = Now.AddMinutes(-2) });
        string path = files.File("reminders.json");
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(original, ReminderStore.JsonOptions); File.WriteAllBytes(path, bytes);
        var settings = new UserSettingsService(new MemorySettings { Data = new() { TimeZoneId = "UTC" } }, new MemoryAutostart());
        var commands = new ReminderCommands(new ReminderStore(path), userSettings: settings);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".bak"));
        Assert.Equal(JsonSerializer.Serialize(original, ReminderStore.JsonOptions), JsonSerializer.Serialize(commands.GetSnapshot(), ReminderStore.JsonOptions));
        Assert.Equal("Europe/Kyiv", settings.GetSnapshot().TimeZoneId);
        Assert.False(settings.ShouldOnboard);
        Assert.Equal(47, settings.GetSnapshot().Personality.MinimumCommentIntervalMinutes);
        Assert.False(settings.GetSnapshot().Personality.PersonalityEnabled);
        Assert.True(commands.Acknowledge("synthetic-pending", false, Now).Success);
        Assert.Equal(bytes, File.ReadAllBytes(path + ".pre-v03-backup"));
        Assert.Equal(bytes, File.ReadAllBytes(path + ".bak"));
        Assert.Equal(original.Reminders.Select(r => (r.Id, r.Time, r.Enabled, r.LastFiredLocalDate)), commands.GetSnapshot().Reminders.Select(r => (r.Id, r.Time, r.Enabled, r.LastFiredLocalDate)));
        Assert.Equal(ReminderAcknowledgment.Dismissed, commands.GetSnapshot().Occurrences.Single().Acknowledgment);
    }

    [Fact]
    public void Legacy_pet_preferences_are_read_without_rewriting_and_cannot_override_saved_settings()
    {
        using var files = new TempFiles(); string path = files.File("legacy-pet.json");
        const string bytes = "{\"AlwaysOnTop\":false,\"CatSize\":125,\"WalkingSpeed\":42,\"AnimationFps\":24}";
        File.WriteAllText(path, bytes);
        var legacy = PetConfiguration.Load(path);
        var memory = new MemorySettings(); var service = new UserSettingsService(memory, new MemoryAutostart());
        service.AdoptExisting(SyntheticReminderFixtures.Schedule());
        service.AdoptLegacyPet(legacy.AlwaysOnTop, legacy.CatSize, legacy.WalkingSpeed, legacy.AnimationFps);
        Assert.False(service.GetSnapshot().PetTopmost); Assert.Equal(125, service.GetSnapshot().CatSize);
        Assert.Equal(42, service.GetSnapshot().WalkingSpeed); Assert.Equal(24, service.GetSnapshot().AnimationFps);
        Assert.Equal(bytes, File.ReadAllText(path)); Assert.Equal(0, memory.Saves);
        Assert.True(service.Save(service.GetSnapshot()).Success);
        service.AdoptLegacyPet(true, 200, 100, 60);
        Assert.Equal(125, service.GetSnapshot().CatSize);
    }

    [Fact]
    public void Settings_version_zero_is_only_normalized_in_memory_with_original_backup_on_save()
    {
        using var files = new TempFiles(); string path = files.File("user-settings.json");
        string draft = JsonSerializer.Serialize(new UserSettings { SchemaVersion = 0, OwnerDisplayName = "Synthetic owner", TimeZoneId = "UTC" }, ReminderDataCodec.JsonOptions);
        File.WriteAllText(path, draft); var store = new UserSettingsStore(path);
        var result = store.Load(); Assert.True(result.CanWrite); Assert.Equal(2, result.Data!.SchemaVersion);
        Assert.Equal(draft, File.ReadAllText(path)); Assert.False(File.Exists(path + ".schema-v0-backup"));
        Assert.True(store.Save(result.Data)); Assert.Equal(draft, File.ReadAllText(path + ".schema-v0-backup"));
        Assert.Equal(2, new UserSettingsStore(path).Load().Data!.SchemaVersion);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Corrupt_or_missing_primary_recovers_a_valid_backup_without_destroying_it(bool missing)
    {
        using var files = new TempFiles(); string path = files.File("user-settings.json");
        const string corrupt = "{invalid";
        if (!missing) File.WriteAllText(path, corrupt);
        string backup = JsonSerializer.Serialize(new UserSettings { OwnerDisplayName = "Synthetic backup", TimeZoneId = "UTC" }, ReminderDataCodec.JsonOptions);
        File.WriteAllText(path + ".bak", backup);
        var store = new UserSettingsStore(path); var loaded = store.Load();
        Assert.True(loaded.CanWrite); Assert.NotNull(loaded.Error); Assert.Equal("Synthetic backup", loaded.Data!.OwnerDisplayName);
        Assert.Equal(backup, File.ReadAllText(path + ".bak"));
        Assert.Equal(!missing, System.IO.Directory.GetFiles(files.Directory, "*.invalid-*").Length == 1);
        Assert.True(store.Save(loaded.Data));
        Assert.Equal(backup, File.ReadAllText(path + ".bak"));
    }

    [Theory]
    [InlineData(99)]
    [InlineData(-1)]
    public void Unsupported_primary_is_never_reset_even_with_a_valid_backup(int version)
    {
        using var files = new TempFiles(); string path = files.File("user-settings.json");
        string future = "{\"SchemaVersion\":" + version + ",\"FutureField\":true}";
        File.WriteAllText(path, future); File.WriteAllText(path + ".bak", JsonSerializer.Serialize(new UserSettings()));
        string backup = File.ReadAllText(path + ".bak"); var store = new UserSettingsStore(path);
        Assert.False(store.Load().CanWrite); Assert.False(store.Save(new()));
        Assert.Equal(future, File.ReadAllText(path)); Assert.Equal(backup, File.ReadAllText(path + ".bak"));
    }

    [Fact]
    public void Corrupt_settings_without_backup_are_preserved_and_block_writes()
    {
        using var files = new TempFiles(); string path = files.File("user-settings.json"); File.WriteAllText(path, "bad bytes");
        var store = new UserSettingsStore(path); var load = store.Load();
        Assert.False(load.CanWrite); Assert.False(store.Save(new())); Assert.Equal("bad bytes", File.ReadAllText(path));
        Assert.Single(System.IO.Directory.GetFiles(files.Directory, "*.invalid-*"));
    }

    [Fact]
    public void Unsupported_backup_is_never_overwritten_by_settings_save()
    {
        using var files = new TempFiles(); string path = files.File("user-settings.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new UserSettings())); File.WriteAllText(path + ".bak", "{\"SchemaVersion\":999}");
        var store = new UserSettingsStore(path); Assert.True(store.Load().CanWrite); Assert.True(store.Save(new UserSettings { OwnerDisplayName = "Synthetic edit" }));
        Assert.Equal("{\"SchemaVersion\":999}", File.ReadAllText(path + ".bak"));
        var compatible = Assert.Single(System.IO.Directory.GetFiles(files.Directory, "*.compatible-*.bak"));
        Assert.Equal("", new UserSettingsStore(compatible).Load().Data!.OwnerDisplayName);
    }

    [Fact]
    public void Recovered_draft_is_not_rewritten_on_load_and_gets_an_exact_backup_before_save()
    {
        using var files = new TempFiles(); string path = files.File("user-settings.json");
        string draft = JsonSerializer.Serialize(new UserSettings { SchemaVersion = 0, OwnerDisplayName = "Synthetic draft" });
        File.WriteAllText(path, "bad primary"); File.WriteAllText(path + ".bak", draft);
        var store = new UserSettingsStore(path); var load = store.Load();
        Assert.True(load.CanWrite); Assert.False(File.Exists(path + ".schema-v0-backup"));
        Assert.True(store.Save(load.Data!)); Assert.Equal(draft, File.ReadAllText(path + ".schema-v0-backup"));
    }

    [Fact]
    public void Invalid_permanent_draft_backup_blocks_replacement_instead_of_being_trusted()
    {
        using var files = new TempFiles(); string path = files.File("user-settings.json");
        string draft = JsonSerializer.Serialize(new UserSettings { SchemaVersion = 0 });
        File.WriteAllText(path, draft); File.WriteAllText(path + ".schema-v0-backup", "damaged snapshot");
        var store = new UserSettingsStore(path); var loaded = store.Load();
        Assert.False(store.Save(loaded.Data!)); Assert.Equal(draft, File.ReadAllText(path));
        Assert.Equal("damaged snapshot", File.ReadAllText(path + ".schema-v0-backup"));
    }

    [Fact]
    public void Failed_settings_commit_rolls_back_memory_and_autostart()
    {
        using var files = new TempFiles(); var store = new FailingSettings(files.File("user-settings.json"));
        var registry = new MemoryAutostart { Value = "original registration" }; var service = new UserSettingsService(store, registry);
        Assert.True(service.SaveOnboardingDraftIfNeeded().Success);
        byte[] previous = File.ReadAllBytes(store.FilePath); store.Fail = true;
        var result = service.Save(service.GetSnapshot() with { OwnerDisplayName = "Unsaved", LaunchAtSignIn = true });
        Assert.False(result.Success); Assert.Contains("Не сохранено", result.Error);
        Assert.Equal("", service.GetSnapshot().OwnerDisplayName); Assert.False(service.GetSnapshot().LaunchAtSignIn);
        Assert.Equal("original registration", registry.Value); Assert.Equal(previous, File.ReadAllBytes(store.FilePath));
        Assert.Equal(previous, File.ReadAllBytes(store.FilePath + ".bak"));
    }

    [Fact]
    public void Autostart_enable_disable_uses_one_adapter_and_errors_prevent_durable_changes()
    {
        var store = new MemorySettings(); var registry = new MemoryAutostart(); var service = new UserSettingsService(store, registry);
        Assert.False(service.GetSnapshot().LaunchAtSignIn); Assert.Equal(0, registry.Writes);
        Assert.True(service.Save(service.GetSnapshot() with { LaunchAtSignIn = true }).Success);
        Assert.Equal("\"synthetic-app.exe\" --pet-only", registry.Value);
        Assert.True(service.Save(service.GetSnapshot()).Success); Assert.Equal(1, registry.Writes);
        Assert.True(service.Save(service.GetSnapshot() with { LaunchAtSignIn = false }).Success); Assert.Null(registry.Value);
        registry.Fail = true;
        int saved = store.Saves;
        Assert.False(service.Save(service.GetSnapshot() with { LaunchAtSignIn = true }).Success);
        Assert.Equal(saved, store.Saves); Assert.False(service.GetSnapshot().LaunchAtSignIn);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Onboarding_completion_or_explicit_skip_is_durable_only_after_success(bool skip)
    {
        var store = new MemorySettings { Fail = true }; var service = new UserSettingsService(store, new MemoryAutostart());
        Assert.False(service.CompleteOnboarding(skip).Success); Assert.True(service.ShouldOnboard);
        store.Fail = false;
        Assert.True(service.CompleteOnboarding(skip).Success); Assert.False(service.ShouldOnboard);
        Assert.True(new UserSettingsService(store, new MemoryAutostart()).GetSnapshot().OnboardingCompleted);
    }

    [Theory]
    [InlineData(null, "Хозяин, Чих требует внимания.")]
    [InlineData("", "Хозяин, Чих требует внимания.")]
    [InlineData("  Саша  ", "Саша, Чих требует внимания.")]
    [InlineData("<script>text</script>", "<script>text</script>, Чих требует внимания.")]
    public void Optional_owner_template_is_literal_text_not_executable_markup(string? name, string expected) => Assert.Equal(expected, DialogueTemplate.Render("{ownerName}, Чих требует внимания.", name));

    [Theory]
    [InlineData("bad\nname", "UTC")]
    [InlineData("Synthetic", "Not/AZone")]
    public void Invalid_settings_never_commit(string name, string zone)
    {
        var store = new MemorySettings(); var service = new UserSettingsService(store, new MemoryAutostart());
        Assert.False(service.Save(new() { OwnerDisplayName = name, TimeZoneId = zone }).Success); Assert.Equal(0, store.Saves);
    }

    [Fact]
    public void Personality_templates_keep_the_catalog_variety_and_use_a_live_optional_name()
    {
        string? owner = null; var personality = new PersonalityService(() => new(), Now, new Random(3), () => owner);
        string[] first = Enumerable.Range(0, DialogueCatalog.Phrases[DialogueCategory.Random].Length).Select(_ => personality.NextPhrase(DialogueCategory.Random)).ToArray();
        Assert.Equal(first.Length, first.Distinct().Count()); Assert.Contains(first, phrase => phrase.Contains("Хозяин"));
        owner = "Synthetic owner";
        string[] next = Enumerable.Range(0, first.Length).Select(_ => personality.NextPhrase(DialogueCategory.Random)).ToArray();
        Assert.Contains(next, phrase => phrase.Contains(owner)); Assert.DoesNotContain(next, phrase => phrase.Contains("{ownerName}"));
    }

    private static T Value<T>(PlannerResult<T> result) { Assert.True(result.Success, result.Message); return result.Value!; }
    private sealed class FailingSettings(string path) : UserSettingsStore(path)
    {
        public bool Fail;
        protected override void Commit(string temporary, string destination) { if (Fail && destination == FilePath) throw new IOException("Synthetic commit failure"); base.Commit(temporary, destination); }
    }
}

internal sealed class TempFiles : IDisposable
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ChihDesktop.R1Tests")) + Path.DirectorySeparatorChar;
    public string Directory { get; } = Path.Combine(Root, Guid.NewGuid().ToString("N"));
    public TempFiles() => System.IO.Directory.CreateDirectory(Directory);
    public string File(string name) => Path.Combine(Directory, name);
    public void Dispose()
    {
        if (!Path.GetFullPath(Directory).StartsWith(Root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe fixture cleanup path.");
        System.IO.Directory.Delete(Directory, true);
    }
}
