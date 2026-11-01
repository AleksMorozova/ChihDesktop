using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ChihDesktop.Infrastructure;

namespace ChihDesktop;

public static class DesktopComposition
{
    // Overrides are for isolated tests. Building/starting the host does not resolve windows,
    // touch user configuration, or register any hosted workers.
    public static IHost CreateHost(IReminderPersistence? persistence = null, TimeProvider? clock = null,
        PlannerDatabaseOptions? plannerDatabase = null, UserSettingsService? userSettings = null,
        string? userDataDirectory = null, IAutostartRegistration? autostart = null, AdminDiagnostics? adminDiagnostics = null)
    {
        if (userDataDirectory is not null && autostart is null)
            throw new ArgumentException("Isolated profiles must supply an autostart adapter.", nameof(autostart));
        string folder = System.IO.Path.GetFullPath(userDataDirectory ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChihDesktop"));
        return new HostBuilder()
        .UseDefaultServiceProvider(options => { options.ValidateScopes = true; options.ValidateOnBuild = true; })
        .ConfigureLogging(logging => logging.ClearProviders().AddDebug().SetMinimumLevel(LogLevel.Information))
        .ConfigureServices(services =>
        {
            services.AddSingleton(adminDiagnostics ?? new AdminDiagnostics(System.IO.Path.Combine(folder, "logs")));
            services.AddSingleton<IHostLifetime, DesktopHostLifetime>();
            services.AddSingleton<TimeProvider>(clock ?? TimeProvider.System);
            services.AddSingleton(_ => userSettings ?? new UserSettingsService(new UserSettingsStore(System.IO.Path.Combine(folder, "user-settings.json"), defaults: () =>
            {
                bool legacy = System.IO.File.Exists(System.IO.Path.Combine(folder, "chih.db"));
                return new UserSettings { TimeZoneId = legacy ? "Europe/Kyiv" : TimeZoneInfo.Local.Id };
            }), autostart ?? new WindowsAutostartRegistration()));
            services.AddPlannerStorage(plannerDatabase ?? new PlannerDatabaseOptions(System.IO.Path.Combine(folder, "chih.db")));
            services.Replace(ServiceDescriptor.Singleton<IPlannerService>(provider => new PlannerService(provider.GetRequiredService<IPlannerTaskStore>(), provider.GetRequiredService<TimeProvider>(),
                () => provider.GetRequiredService<UserSettingsService>().GetSnapshot().TimeZoneId)));
            services.AddSingleton<IReminderPersistence>(provider => persistence ??
                new ReminderStore(System.IO.Path.Combine(folder, "reminders.json"), provider.GetRequiredService<TimeProvider>()));
            services.AddSingleton<ReminderCommands>();
            services.AddSingleton<IReminderCommands>(provider => provider.GetRequiredService<ReminderCommands>());
            services.AddSingleton(provider => new PersonalityService(
                provider.GetRequiredService<IReminderCommands>().GetPersonalityOptions,
                provider.GetRequiredService<TimeProvider>().GetUtcNow(), ownerName: () => provider.GetRequiredService<UserSettingsService>().GetSnapshot().OwnerDisplayName));
            services.AddSingleton<PetConfiguration>(provider => PetConfiguration.From(provider.GetRequiredService<UserSettingsService>().GetSnapshot()));
            services.AddSingleton<Func<Window, IReminderPresenter>>(_ => owner => new ReminderBubble(owner));
            services.AddSingleton<ReminderControllerFactory>();
            services.AddSingleton<AdminWindowManager>();
            services.AddSingleton<TaskListWindowManager>();
            services.AddSingleton<AdminApplicationApi>();
            services.AddSingleton<UserSettingsWindowManager>();
            services.AddSingleton<CatWindow>();
        }).Build();
    }
}

internal sealed class DesktopHostLifetime : IHostLifetime
{
    public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class ReminderControllerFactory(IReminderCommands commands, PersonalityService personality,
    TimeProvider clock, Func<Window, IReminderPresenter> presenter, ILogger<ReminderController> logger, IPlannerService planner, UserSettingsService settings, TaskListWindowManager tasks, AdminDiagnostics diagnostics)
{
    public ReminderController Create(Window owner, Func<bool> interactionBlocked, Func<CatState> state) =>
        new(owner, interactionBlocked, state, commands, presenter(owner), personality, clock, logger, planner, settings, tasks, diagnostics);
}
