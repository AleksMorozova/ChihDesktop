using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using System.Runtime.InteropServices;

namespace ChihDesktop.Mac;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.SequenceEqual(new[] { "--self-test" })) return PrototypeChecks.Run();
        if (args.FirstOrDefault()?.StartsWith("--alpha-", StringComparison.Ordinal) == true || args.SequenceEqual(new[] { "--self-test-packaging" }))
            return AlphaPackaging.Run(args);
        if (!OperatingSystem.IsMacOS() && !args.Contains("--allow-other-platforms"))
        {
            Console.Error.WriteLine("This is the macOS prototype. Use --self-test on any OS, or --allow-other-platforms for explicit non-Mac visual development.");
            return 2;
        }
        Console.WriteLine($"ChihDesktop macOS alpha {typeof(Program).Assembly.GetName().Version}; {RuntimeInformation.OSDescription}; process {RuntimeInformation.ProcessArchitecture}; .NET {Environment.Version}");
        return AppBuilder.Configure<MacApp>().UsePlatformDetect()
            .With(new MacOSPlatformOptions { ShowInDock = true }) // Keep native Quit recovery while prototyping.
            .LogToTrace()
            .StartWithClassicDesktopLifetime(args);
    }
}

internal sealed class MacApp : Application
{
    public override void Initialize()
    {
        Name = "ChihDesktop";
        Styles.Add(new FluentTheme());
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            desktop.MainWindow = new MacPetWindow();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
