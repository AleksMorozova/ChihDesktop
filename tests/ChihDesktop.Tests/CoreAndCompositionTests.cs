using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChihDesktop.Tests;

public sealed class CoreAndCompositionTests
{
    [Fact]
    public void Core_targets_net10_and_has_no_project_package_or_windows_dependencies()
    {
        var assembly = typeof(ReminderCommands).Assembly;
        Assert.Equal("ChihDesktop.Core", assembly.GetName().Name);
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference =>
            reference.Name is "ChihDesktop" or "WindowsBase" or "PresentationCore" or "PresentationFramework" ||
            reference.Name!.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal));
        string root = SpriteOutputPolicy.FindWorkspace(AppContext.BaseDirectory);
        var project = XDocument.Load(Path.Combine(root, "ChihDesktop.Core", "ChihDesktop.Core.csproj"));
        Assert.Equal("net10.0", project.Descendants("TargetFramework").Single().Value);
        Assert.Empty(project.Descendants("ProjectReference"));
        Assert.Empty(project.Descendants("PackageReference"));
        Assert.Empty(project.Descendants("UseWPF"));
        Assert.DoesNotContain(assembly.GetTypes(), type => type.Name == nameof(ReminderStore));
    }

    [Fact]
    public void Core_types_have_no_native_entry_points_or_ui_types_in_public_contracts()
    {
        foreach (var type in typeof(ReminderCommands).Assembly.GetTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                Assert.Null(method.GetCustomAttribute<DllImportAttribute>());
            foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                IEnumerable<Type> types = member switch
                {
                    MethodInfo method => method.GetParameters().Select(p => p.ParameterType).Append(method.ReturnType),
                    PropertyInfo property => [property.PropertyType],
                    FieldInfo field => [field.FieldType],
                    _ => []
                };
                foreach (var exposed in types) Assert.DoesNotContain("System.Windows", exposed.ToString());
            }
        }
    }

    [Fact]
    public void Core_consumes_persistence_interface_instead_of_json_file_implementation()
    {
        Assert.Equal(typeof(IReminderPersistence), typeof(ReminderCommands).GetConstructors().Single().GetParameters()[0].ParameterType);
        Assert.Equal(typeof(IReminderPersistence), typeof(ReminderScheduler).GetConstructors().Single().GetParameters()[0].ParameterType);
        Assert.NotEqual(typeof(ReminderCommands).Assembly, typeof(ReminderStore).Assembly);
        Assert.True(typeof(IReminderPersistence).IsAssignableFrom(typeof(ReminderStore)));
    }

    [Fact]
    public void Injected_clock_controls_implicit_acknowledgment_and_snooze_times()
    {
        var clock = new ManualClock(new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));
        var persistence = new MemoryPersistence();
        IReminderCommands commands = new ReminderCommands(persistence, clock);
        commands.Tick(clock.GetUtcNow(), TimeSpan.Zero);
        var pair = commands.TakeNextGroup(clock.GetUtcNow())!;
        Assert.Equal(2, pair.Count);
        clock.Advance(TimeSpan.FromMinutes(3));
        Assert.True(commands.Acknowledge(pair[0].Id, true).Success);
        Assert.True(commands.Snooze(pair[1].Id).Success);
        var state = commands.GetSnapshot();
        Assert.Equal(clock.GetUtcNow(), state.Occurrences[0].AcknowledgedAtUtc);
        Assert.Equal(clock.GetUtcNow().AddMinutes(10), state.Occurrences[1].SnoozedUntilUtc);
    }

    [Fact]
    public void Core_runs_with_memory_persistence_and_rolls_back_without_file_access()
    {
        var persistence = new MemoryPersistence();
        var clock = new ManualClock(new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));
        var commands = new ReminderCommands(persistence, clock);
        commands.Tick(clock.GetUtcNow(), TimeSpan.Zero);
        var pair = commands.TakeNextGroup(clock.GetUtcNow())!;
        persistence.FailWrites = true;
        Assert.False(commands.Acknowledge(pair[0].Id, true).Success);
        Assert.All(commands.GetSnapshot().Occurrences, item => Assert.Equal(ReminderAcknowledgment.Pending, item.Acknowledgment));
        Assert.All(persistence.Stored.Occurrences, item => Assert.Equal(ReminderAcknowledgment.Pending, item.Acknowledgment));
    }

    [Fact]
    public async Task Host_start_stop_has_no_workers_and_does_not_eagerly_load_user_data_or_windows()
    {
        var persistence = new MemoryPersistence();
        using var host = DesktopComposition.CreateHost(persistence, userSettings: TestSettings.Create());
        await host.StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, persistence.LoadCalls);
        Assert.Empty(host.Services.GetServices<IHostedService>());
        Assert.IsType<DesktopHostLifetime>(host.Services.GetRequiredService<IHostLifetime>());
        Assert.NotNull(host.Services.GetRequiredService<ILogger<ReminderController>>());
        await host.StopAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, persistence.LoadCalls);
    }

    [Fact]
    public void Composition_resolves_singleton_commands_clock_and_personality_against_supplied_persistence()
    {
        var persistence = new MemoryPersistence();
        var clock = new ManualClock(new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));
        using var host = DesktopComposition.CreateHost(persistence, clock, userSettings: TestSettings.Create());
        var commands = host.Services.GetRequiredService<IReminderCommands>();
        Assert.Same(commands, host.Services.GetRequiredService<ReminderCommands>());
        Assert.Same(clock, host.Services.GetRequiredService<TimeProvider>());
        Assert.Same(persistence, host.Services.GetRequiredService<IReminderPersistence>());
        Assert.NotNull(host.Services.GetRequiredService<PersonalityService>());
        Assert.NotNull(host.Services.GetRequiredService<ReminderControllerFactory>());
        Assert.Equal(1, persistence.LoadCalls);
        Assert.Equal(new[] { "item-a", "item-b" }, commands.GetSnapshot().Reminders.Select(r => r.Id));
    }

    [Fact]
    public void Single_instance_lease_rejects_second_owner_and_releases_after_dispose()
    {
        string name = @"Local\ChihDesktop.Test." + Guid.NewGuid().ToString("N");
        using (var first = SingleInstanceLease.TryAcquire(name))
        {
            Assert.NotNull(first);
            Assert.Null(SingleInstanceLease.TryAcquire(name));
        }
        using var next = SingleInstanceLease.TryAcquire(name);
        Assert.NotNull(next);
        next.Dispose(); // Explicit and final disposal must be idempotent.
    }

    [Fact]
    public void Presenter_adapter_keeps_group_independent_and_close_does_not_acknowledge() => OnSta(() =>
    {
        var clock = new ManualClock(new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));
        var commands = new ReminderCommands(new MemoryPersistence(), clock);
        var presenter = new TestPresenter();
        var owner = new Window(); // No native window is shown and no sprite is constructed.
        using (var controller = new ReminderController(owner, () => false, () => CatState.Idle, commands, presenter,
                   new PersonalityService(commands.GetPersonalityOptions, clock.GetUtcNow()), clock, NullLogger<ReminderController>.Instance))
        {
            Assert.Equal(2, presenter.Occurrences.Count);
            presenter.Confirm(presenter.Occurrences[0].Id);
            Assert.Equal(ReminderAcknowledgment.Completed, presenter.Occurrences[0].Acknowledgment);
            Assert.Equal(ReminderAcknowledgment.Pending, presenter.Occurrences[1].Acknowledgment);
            presenter.ClosePresentation();
            Assert.False(presenter.IsVisible);
            Assert.Empty(commands.GetPresentation());
            Assert.Equal(ReminderAcknowledgment.Pending, commands.GetSnapshot().Occurrences[1].Acknowledgment);
        }
        Assert.True(presenter.Disposed);
        owner.Close();
    });

    [Fact]
    public void Presenter_adapter_restores_pending_checkbox_and_exposes_failed_save() => OnSta(() =>
    {
        var clock = new ManualClock(new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));
        var persistence = new MemoryPersistence();
        var commands = new ReminderCommands(persistence, clock);
        var presenter = new TestPresenter();
        var owner = new Window();
        using (var controller = new ReminderController(owner, () => false, () => CatState.Idle, commands, presenter,
                   new PersonalityService(commands.GetPersonalityOptions, clock.GetUtcNow()), clock, NullLogger<ReminderController>.Instance))
        {
            persistence.FailWrites = true;
            presenter.Confirm(presenter.Occurrences[0].Id);
            Assert.Equal(2, presenter.Occurrences.Count);
            Assert.All(presenter.Occurrences, item => Assert.Equal(ReminderAcknowledgment.Pending, item.Acknowledgment));
            Assert.NotNull(presenter.Error);
            Assert.True(presenter.IsVisible);
        }
        owner.Close();
    });

    private static void OnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "STA adapter test timed out.");
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan interval) => now += interval;
    }

    private sealed class MemoryPersistence : IReminderPersistence
    {
        public string FilePath => "memory://synthetic-fixture";
        public string? Error { get; private set; }
        public bool CanWrite => true;
        public bool FailWrites { get; set; }
        public int LoadCalls { get; private set; }
        public ReminderLoadResult? LoadOutcome { get; private set; }
        public ReminderData Stored { get; private set; } = new()
        {
            QuietHoursEnabled = false,
            Reminders =
            [
                new() { Id = "item-a", Title = "Пункт A", Time = new(14, 0), BubbleGroup = "pair", MessageVariants = ["Проверка A"] },
                new() { Id = "item-b", Title = "Пункт B", Time = new(14, 0), BubbleGroup = "pair", MessageVariants = ["Проверка B"] }
            ]
        };
        public ReminderLoadResult Load()
        {
            LoadCalls++;
            return LoadOutcome = new(ReminderLoadStatus.Primary, ReminderDataCodec.Copy(Stored), null, true);
        }
        public bool Save(ReminderData data)
        {
            Error = FailWrites ? "Synthetic write failure" : null;
            if (FailWrites) return false;
            Stored = ReminderDataCodec.Copy(data);
            return true;
        }
    }

    private sealed class TestPresenter : IReminderPresenter
    {
        public event Action<ReminderOccurrence, string>? ActionRequested;
        public event Action? DialogueDismissRequested;
        public IReadOnlyList<ReminderOccurrence> Occurrences { get; private set; } = [];
        public bool IsVisible { get; private set; }
        public bool IsDialogue => IsVisible && Occurrences.Count == 0 && !IsStoreNotice;
        public bool IsStoreNotice { get; private set; }
        public bool Disposed { get; private set; }
        public string? Error { get; private set; }
        public void Present(IReadOnlyList<ReminderOccurrence> occurrences, string displayMessage)
        { Occurrences = occurrences; IsVisible = true; IsStoreNotice = false; }
        public void PresentDialogue(string mood, string text) { Occurrences = []; IsVisible = true; }
        public void PresentStoreNotice(string text) { PresentDialogue("notice", text); IsStoreNotice = true; }
        public void ShowPersistenceError(string? error) => Error = error;
        public void RefreshItems(IReadOnlyList<ReminderOccurrence>? latest = null) { if (latest is not null) Occurrences = latest; }
        public void HideReminder() { Occurrences = []; IsVisible = false; IsStoreNotice = false; }
        public void Confirm(string id) => ActionRequested?.Invoke(Occurrences.Single(o => o.Id == id), "done");
        public void ClosePresentation() => DialogueDismissRequested?.Invoke();
        public void Dispose() { Disposed = true; HideReminder(); }
    }
}
