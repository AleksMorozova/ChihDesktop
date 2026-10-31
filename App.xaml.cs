using System.ComponentModel;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChihDesktop;

public partial class App : Application
{
    private SingleInstanceLease? instance;
    private IHost? host;
    private ILogger<App>? logger;
    private bool stopping, shutdownReady;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            if (e.Args.Contains("--remove-autostart")) { new WindowsAutostartRegistration().Write(null); Shutdown(); return; }
            // Check before host/service construction so a second instance never writes user data.
            instance = SingleInstanceLease.TryAcquire();
            if (instance is null) { Shutdown(); return; }
            host = DesktopComposition.CreateHost();
            logger = host.Services.GetRequiredService<ILogger<App>>();
            await host.StartAsync();
            var commands = host.Services.GetRequiredService<IReminderCommands>(); // Read existing records before a new planner DB can mark the profile as existing.
            var settings = host.Services.GetRequiredService<UserSettingsService>();
            if (settings.ShouldOnboard) settings.SaveOnboardingDraftIfNeeded();
            if (settings.GetSnapshot().OnboardingCompleted)
            {
                var legacyPet = PetConfiguration.Load();
                settings.AdoptLegacyPet(legacyPet.AlwaysOnTop, legacyPet.CatSize, legacyPet.WalkingSpeed, legacyPet.AnimationFps);
            }
            var planner = await host.Services.GetRequiredService<IPlannerDatabaseInitializer>().InitializeAsync();
            if (!planner.Success) logger.LogError("Planner unavailable: {Reason}", planner.Message);
            MainWindow = host.Services.GetRequiredService<CatWindow>();
            MainWindow.Closing += OnMainWindowClosing;
            MainWindow.Show();
            if (settings.ShouldOnboard) host.Services.GetRequiredService<UserSettingsWindowManager>().Show(true);
            else if (!settings.CanWrite) host.Services.GetRequiredService<UserSettingsWindowManager>().Show();
            else if (!settings.GetSnapshot().StartPetOnly && !e.Args.Contains("--pet-only")) host.Services.GetRequiredService<AdminWindowManager>().Show();
            logger.LogInformation("ChihDesktop started.");
        }
        catch (Exception ex)
        {
            logger?.LogError("Startup failed ({ExceptionType}).", ex.GetType().Name);
            MessageBox.Show("Не удалось запустить Чиха. " + ex.Message, "ChihDesktop", MessageBoxButton.OK, MessageBoxImage.Error);
            await StopHostAsync();
            Shutdown(1);
        }
    }

    private async void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        if (shutdownReady) return;
        e.Cancel = true;
        if (stopping) return;
        stopping = true;
        try
        {
            (MainWindow as CatWindow)?.StopApplicationServices();
            await StopHostAsync();
        }
        catch (Exception ex) { System.Diagnostics.Trace.TraceError("Window cleanup failed: {0}", ex.GetType().Name); }
        finally
        {
            shutdownReady = true;
            MainWindow.Close();
        }
    }

    private async Task StopHostAsync()
    {
        var stoppingHost = host;
        if (stoppingHost is null) return;
        host = null;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await stoppingHost.StopAsync(timeout.Token);
        }
        catch (Exception ex) { logger?.LogWarning("Host stop failed ({ExceptionType}).", ex.GetType().Name); }
        finally
        {
            try
            {
                if (stoppingHost is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync();
                else stoppingHost.Dispose();
            }
            catch (Exception ex) { System.Diagnostics.Trace.TraceError("Host disposal failed: {0}", ex.GetType().Name); }
            logger = null;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Normal window close awaited StopHostAsync. This is a fallback for abrupt WPF shutdown.
        try
        {
            (MainWindow as CatWindow)?.StopApplicationServices();
            host?.Dispose();
        }
        finally
        {
            instance?.Dispose();
            base.OnExit(e);
        }
    }
}
