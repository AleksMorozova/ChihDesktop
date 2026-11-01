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
    private AdminDiagnostics diagnostics = AdminDiagnostics.Disabled;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        diagnostics = AdminDiagnostics.ForCurrentUser(); diagnostics.Started();
        try
        {
            if (e.Args.Contains("--remove-autostart")) { new WindowsAutostartRegistration().Write(null); Shutdown(); return; }
            // Check before host/service construction so a second instance never writes user data.
            instance = SingleInstanceLease.TryAcquire();
            if (instance is null) { diagnostics.Write("instance.blocked"); Shutdown(); return; }
            diagnostics.Write("instance.acquired");
            host = DesktopComposition.CreateHost(adminDiagnostics: diagnostics);
            diagnostics.Write("host.created");
            logger = host.Services.GetRequiredService<ILogger<App>>();
            await host.StartAsync();
            diagnostics.Write("host.started");
            var commands = host.Services.GetRequiredService<IReminderCommands>(); // Read existing records before a new planner DB can mark the profile as existing.
            var settings = host.Services.GetRequiredService<UserSettingsService>();
            if (settings.ShouldOnboard) settings.SaveOnboardingDraftIfNeeded();
            if (settings.GetSnapshot().OnboardingCompleted)
            {
                var legacyPet = PetConfiguration.Load();
                settings.AdoptLegacyPet(legacyPet.AlwaysOnTop, legacyPet.CatSize, legacyPet.WalkingSpeed, legacyPet.AnimationFps);
            }
            diagnostics.Write("planner.initialize.start");
            var planner = await host.Services.GetRequiredService<IPlannerDatabaseInitializer>().InitializeAsync();
            diagnostics.Write("planner.initialize.complete", new { planner.Success });
            if (!planner.Success) logger.LogError("Planner unavailable: {Reason}", planner.Message);
            MainWindow = host.Services.GetRequiredService<CatWindow>();
            MainWindow.Closing += OnMainWindowClosing;
            MainWindow.Show();
            diagnostics.Window("pet.shown", MainWindow);
            if (settings.ShouldOnboard) host.Services.GetRequiredService<UserSettingsWindowManager>().Show(true);
            else if (!settings.CanWrite) host.Services.GetRequiredService<UserSettingsWindowManager>().Show();
            else if (!settings.GetSnapshot().StartPetOnly && !e.Args.Contains("--pet-only")) host.Services.GetRequiredService<AdminWindowManager>().Show();
            logger.LogInformation("ChihDesktop started.");
        }
        catch (Exception ex)
        {
            diagnostics.Write("app.start.failed", error: ex);
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
        diagnostics.Write("app.close.start");
        try
        {
            (MainWindow as CatWindow)?.StopApplicationServices();
            await StopHostAsync();
        }
        catch (Exception ex) { System.Diagnostics.Trace.TraceError("Window cleanup failed: {0}", ex.GetType().Name); }
        finally
        {
            // StopAsync/DisposeAsync can complete synchronously. Never re-enter Window.Close
            // before this Closing event has unwound (verified real Windows crash).
            _ = MainWindow.Dispatcher.BeginInvoke(() =>
            {
                shutdownReady = true;
                diagnostics.Write("app.close.ready");
                MainWindow.Close();
            });
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
        diagnostics.Write("app.exit", new { e.ApplicationExitCode });
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
