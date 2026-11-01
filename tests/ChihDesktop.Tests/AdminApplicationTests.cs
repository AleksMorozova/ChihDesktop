using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ChihDesktop.Tests;

public sealed class AdminApplicationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly AdminApplicationApi.ReminderInput Input = new() { Title = "Synthetic new reminder", Time = "10:10", Recurrence = ReminderRecurrence.Daily, WeeklyDay = DayOfWeek.Monday, IntervalMinutes = 2, Enabled = false, BypassQuietHours = false };

    [Fact]
    public async Task React_bridge_reminder_CRUD_preserves_unrelated_definitions_history_and_restart()
    {
        using var files = new TempFiles(); string profile = files.File("profile"); Directory.CreateDirectory(profile);
        var original = SyntheticReminderFixtures.Schedule();
        File.WriteAllText(Path.Combine(profile, "reminders.json"), JsonSerializer.Serialize(original, ReminderDataCodec.JsonOptions));
        using (var host = DesktopComposition.CreateHost(userDataDirectory: profile, autostart: new MemoryAutostart(), clock: new Clock()))
        {
            var commands = host.Services.GetRequiredService<IReminderCommands>();
            var unrelated = JsonSerializer.Serialize(commands.GetSnapshot().Reminders, ReminderDataCodec.JsonOptions);
            using var bridge = Bridge(host.Services);
            var created = Read<AdminApplicationApi.ReminderDto>(await Send(bridge, "reminders.create", new { reminder = Input }));
            Assert.False(created.Definition.Enabled);
            var edited = Read<AdminApplicationApi.ReminderDto>(await Send(bridge, "reminders.update", new { id = created.Id, expectedVersion = created.Version, reminder = Input with { Title = "Synthetic edited", Time = "11:15" } }));
            Assert.Equal("11:15", edited.Definition.Time);
            Assert.True((await Send(bridge, "reminders.enable", new { id = edited.Id, expectedVersion = edited.Version, enabled = true })).Success);
            var snapshot = commands.GetSnapshot();
            Assert.Equal(unrelated, JsonSerializer.Serialize(snapshot.Reminders.Where(r => r.Id != created.Id), ReminderDataCodec.JsonOptions));
            Assert.Equal(original.Reminders.Select(r => r.Id), snapshot.Reminders.Where(r => r.Id != created.Id).Select(r => r.Id));
        }
        using var restarted = DesktopComposition.CreateHost(userDataDirectory: profile, autostart: new MemoryAutostart(), clock: new Clock());
        var persisted = restarted.Services.GetRequiredService<IReminderCommands>().GetSnapshot().Reminders.Single(r => r.Title == "Synthetic edited");
        Assert.True(persisted.Enabled); Assert.Equal(new TimeOnly(11, 15), persisted.Time);
        using var nextBridge = Bridge(restarted.Services);
        var list = Document(await Send(nextBridge, "reminders.list", new { }));
        var row = list.GetProperty("definitions").EnumerateArray().Single(r => r.GetProperty("id").GetString() == persisted.Id);
        Assert.True((await Send(nextBridge, "reminders.delete", new { id = persisted.Id, expectedVersion = row.GetProperty("version").GetString() })).Success);
        Assert.DoesNotContain(restarted.Services.GetRequiredService<IReminderCommands>().GetSnapshot().Reminders, r => r.Id == persisted.Id);
    }

    [Fact]
    public async Task Grouped_items_acknowledge_independently_snooze_and_dismiss_survive_restart_and_window_disposal()
    {
        using var files = new TempFiles(); string profile = files.File("profile"); Directory.CreateDirectory(profile);
        var data = ReminderData.CreateDefaults(); data.TimeZoneId = "UTC"; data.QuietHoursEnabled = false;
        data.Reminders = [new() { Id = "synthetic-one", Title = "Synthetic one", Time = new(10, 0), BubbleGroup = "synthetic-group" }, new() { Id = "synthetic-two", Title = "Synthetic two", Time = new(10, 0), BubbleGroup = "synthetic-group" }];
        File.WriteAllText(Path.Combine(profile, "reminders.json"), JsonSerializer.Serialize(data, ReminderDataCodec.JsonOptions));
        using (var host = DesktopComposition.CreateHost(userDataDirectory: profile, autostart: new MemoryAutostart(), clock: new Clock()))
        {
            var commands = host.Services.GetRequiredService<IReminderCommands>(); commands.Tick(Now, TimeSpan.Zero);
            var items = commands.GetSnapshot().Occurrences; Assert.Equal(2, items.Count);
            using var bridge = Bridge(host.Services);
            Assert.True((await Send(bridge, "reminders.snooze", new { id = items[1].Id })).Success);
            Assert.Equal(Now.AddMinutes(10), commands.GetSnapshot().Occurrences[1].SnoozedUntilUtc);
            Assert.True((await Send(bridge, "reminders.acknowledge", new { id = items[0].Id, completed = true })).Success);
            Assert.Equal(ReminderAcknowledgment.Pending, commands.GetSnapshot().Occurrences[1].Acknowledgment);
            Assert.True((await Send(bridge, "reminders.acknowledge", new { id = items[1].Id, completed = false })).Success);
        }
        using var restarted = DesktopComposition.CreateHost(userDataDirectory: profile, autostart: new MemoryAutostart(), clock: new Clock());
        var saved = restarted.Services.GetRequiredService<IReminderCommands>(); saved.Tick(Now, TimeSpan.Zero);
        Assert.Equal(2, saved.GetSnapshot().Occurrences.Count);
        Assert.Equal(new[] { ReminderAcknowledgment.Completed, ReminderAcknowledgment.Dismissed }, saved.GetSnapshot().Occurrences.Select(r => r.Acknowledgment));
    }

    [Fact]
    public async Task Settings_use_same_store_preserve_unexposed_preferences_and_reject_stale_edits()
    {
        using var files = new TempFiles(); string profile = files.File("profile");
        using (var host = DesktopComposition.CreateHost(userDataDirectory: profile, autostart: new MemoryAutostart()))
        {
            var settings = host.Services.GetRequiredService<UserSettingsService>();
            Assert.True(settings.Save(settings.GetSnapshot() with { CatSize = 188, WalkingSpeed = 88, AnimationFps = 20 }).Success);
            using var bridge = Bridge(host.Services);
            var current = Document(await Send(bridge, "settings.get", new { }));
            var input = current.GetProperty("settings").Deserialize<AdminApplicationApi.SettingsInput>(AdminBridge.JsonOptions)!;
            var result = await Send(bridge, "settings.save", new { expectedVersion = current.GetProperty("version").GetString(), settings = input with { OwnerDisplayName = "Synthetic owner <literal>", TimeZoneId = "UTC", PetTopmost = false } });
            Assert.True(result.Success, result.Error?.Message);
            Assert.False(settings.GetSnapshot().PetTopmost); Assert.Equal(188, settings.GetSnapshot().CatSize); Assert.Equal(88, settings.GetSnapshot().WalkingSpeed); Assert.Equal(20, settings.GetSnapshot().AnimationFps);
            var stale = await Send(bridge, "settings.save", new { expectedVersion = current.GetProperty("version").GetString(), settings = input });
            Assert.False(stale.Success); Assert.Equal("Conflict", stale.Error?.Code);
        }
        using var restarted = DesktopComposition.CreateHost(userDataDirectory: profile, autostart: new MemoryAutostart());
        Assert.Equal("Synthetic owner <literal>", restarted.Services.GetRequiredService<UserSettingsService>().GetSnapshot().OwnerDisplayName);
        Assert.Equal("UTC", restarted.Services.GetRequiredService<UserSettingsService>().GetSnapshot().TimeZoneId);
    }

    [Theory]
    [InlineData("reminders.create", "{\"reminder\":null}")]
    [InlineData("reminders.enable", "{\"id\":\"\",\"expectedVersion\":\"bad\",\"enabled\":true}")]
    [InlineData("reminders.acknowledge", "{\"id\":\"anything\",\"completed\":true,\"sql\":\"forbidden\"}")]
    [InlineData("settings.save", "{\"settings\":null}")]
    public async Task Invalid_native_payloads_are_explicit_failures(string operation, string payload)
    {
        using var files = new TempFiles(); using var host = DesktopComposition.CreateHost(userDataDirectory: files.File("profile"), autostart: new MemoryAutostart());
        using var bridge = Bridge(host.Services);
        var result = await Send(bridge, operation, JsonSerializer.Deserialize<JsonElement>(payload));
        Assert.False(result.Success); Assert.Equal("InvalidPayload", result.Error?.Code);
    }

    [Fact]
    public async Task Stale_reminder_edits_do_not_overwrite_native_changes()
    {
        using var files = new TempFiles(); using var host = DesktopComposition.CreateHost(userDataDirectory: files.File("profile"), autostart: new MemoryAutostart()); using var bridge = Bridge(host.Services);
        var created = Read<AdminApplicationApi.ReminderDto>(await Send(bridge, "reminders.create", new { reminder = Input }));
        Assert.True(host.Services.GetRequiredService<IReminderCommands>().SetReminderEnabled(created.Id, true).Success);
        var stale = await Send(bridge, "reminders.update", new { id = created.Id, expectedVersion = created.Version, reminder = Input });
        Assert.Equal("Conflict", stale.Error?.Code); Assert.True(host.Services.GetRequiredService<IReminderCommands>().GetSnapshot().Reminders.Single().Enabled);
    }

    [Fact]
    public async Task Reminder_update_write_failure_returns_error_and_rolls_back_disk_and_memory()
    {
        using var files = new TempFiles(); var store = new FaultingReminderStore(files.File("reminders.json"));
        using var host = DesktopComposition.CreateHost(persistence: store, userDataDirectory: files.File("profile"), autostart: new MemoryAutostart());
        using var bridge = Bridge(host.Services);
        var created = Read<AdminApplicationApi.ReminderDto>(await Send(bridge, "reminders.create", new { reminder = Input }));
        byte[] before = File.ReadAllBytes(store.FilePath); store.FailPrimaryCommit = true;
        var failed = await Send(bridge, "reminders.update", new { id = created.Id, expectedVersion = created.Version, reminder = Input with { Title = "Must not commit" } });
        Assert.False(failed.Success); Assert.Equal("PersistenceOrValidation", failed.Error?.Code);
        Assert.Equal(before, File.ReadAllBytes(store.FilePath));
        Assert.Equal(Input.Title, host.Services.GetRequiredService<IReminderCommands>().GetSnapshot().Reminders.Single().Title);
    }

    [Fact]
    public async Task Settings_write_failure_is_not_success_and_rolls_back_autostart()
    {
        using var files = new TempFiles(); var store = new MemorySettings(); var registry = new MemoryAutostart();
        var settings = new UserSettingsService(store, registry);
        using var host = DesktopComposition.CreateHost(userSettings: settings, userDataDirectory: files.File("profile"), autostart: registry);
        using var bridge = Bridge(host.Services);
        var snapshot = Document(await Send(bridge, "settings.get", new { }));
        var input = snapshot.GetProperty("settings").Deserialize<AdminApplicationApi.SettingsInput>(AdminBridge.JsonOptions)!;
        store.Fail = true;
        var failed = await Send(bridge, "settings.save", new { expectedVersion = snapshot.GetProperty("version").GetString(), settings = input with { OwnerDisplayName = "Must not persist", LaunchAtSignIn = true } });
        Assert.False(failed.Success); Assert.Equal("PersistenceOrValidation", failed.Error?.Code);
        Assert.Equal("", settings.GetSnapshot().OwnerDisplayName); Assert.False(settings.GetSnapshot().LaunchAtSignIn); Assert.Null(registry.Value);
    }

    [Fact]
    public async Task Editing_another_schedule_does_not_restart_the_two_minute_break_timer()
    {
        using var files = new TempFiles(); using var host = DesktopComposition.CreateHost(userDataDirectory: files.File("profile"), autostart: new MemoryAutostart(), clock: new Clock());
        var commands = host.Services.GetRequiredService<IReminderCommands>();
        Assert.True(commands.CreateReminder(new() { Id = "synthetic-break", Title = "Synthetic break", Recurrence = ReminderRecurrence.RunningInterval, IntervalMinutes = 2 }).Success);
        commands.Tick(Now, TimeSpan.FromSeconds(30)); commands.Tick(Now, TimeSpan.FromSeconds(30));
        using var bridge = Bridge(host.Services);
        var created = Read<AdminApplicationApi.ReminderDto>(await Send(bridge, "reminders.create", new { reminder = Input }));
        var updated = Read<AdminApplicationApi.ReminderDto>(await Send(bridge, "reminders.update", new { id = created.Id, expectedVersion = created.Version, reminder = Input with { Title = "Synthetic changed" } }));
        Assert.True((await Send(bridge, "reminders.enable", new { id = updated.Id, expectedVersion = updated.Version, enabled = false })).Success);
        Assert.True((await Send(bridge, "reminders.delete", new { id = updated.Id, expectedVersion = updated.Version })).Success);
        commands.Tick(Now, TimeSpan.FromSeconds(30)); commands.Tick(Now, TimeSpan.FromSeconds(30));
        Assert.Equal("synthetic-break", commands.GetSnapshot().Occurrences.Single().ReminderId);
    }

    private static AdminBridge Bridge(IServiceProvider services) => new(services.GetRequiredService<IPlannerService>(), application: services.GetRequiredService<AdminApplicationApi>());
    private static Task<AdminResponse> Send(AdminBridge bridge, string operation, object payload) => bridge.HandleAsync(JsonSerializer.Serialize(new { protocolVersion = 1, requestId = Guid.NewGuid().ToString(), operation, payload }, AdminBridge.JsonOptions), TestContext.Current.CancellationToken);
    private static JsonElement Document(AdminResponse response) { Assert.True(response.Success, response.Error?.Message); using var doc = JsonDocument.Parse(AdminBridge.Serialize(response)); return doc.RootElement.GetProperty("data").Clone(); }
    private static T Read<T>(AdminResponse response) => Document(response).Deserialize<T>(AdminBridge.JsonOptions)!;
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class FaultingReminderStore(string path) : ReminderStore(path)
    {
        public bool FailPrimaryCommit;
        protected override void CommitFile(string temporary, string destination)
        {
            if (FailPrimaryCommit && destination == FilePath) throw new IOException("Synthetic admin write failure");
            base.CommitFile(temporary, destination);
        }
    }
}
