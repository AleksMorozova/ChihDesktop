using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;

namespace ChihDesktop;

// Workflow metadata only. Never pass messages, settings, reminder/task data or exception messages here.
public sealed class AdminDiagnostics
{
    private readonly object gate = new();
    public static AdminDiagnostics Disabled { get; } = new(null);
    public string? LogPath { get; }
    public AdminDiagnostics(string? directory)
    {
        if (directory is not null) LogPath = Path.Combine(directory, $"admin-opening-{DateTime.UtcNow:yyyyMMdd}-{Environment.ProcessId}.jsonl");
    }
    public static AdminDiagnostics ForCurrentUser() => new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChihDesktop", "logs"));
    public void Write(string stage, object? detail = null, Exception? error = null)
    {
        if (LogPath is null) return;
        try
        {
            lock (gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 2 * 1024 * 1024) return;
                var methods = error is null ? null : new StackTrace(error, false).GetFrames()?.Take(12).Select(f => f.GetMethod()?.DeclaringType?.FullName + "." + f.GetMethod()?.Name).ToArray();
                File.AppendAllText(LogPath, JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, pid = Environment.ProcessId, stage, detail,
                    exceptionType = error?.GetType().FullName, hresult = error?.HResult, innerType = error?.InnerException?.GetType().FullName, methods }) + Environment.NewLine);
            }
        }
        catch (Exception) { /* Diagnostics must not stop the pet or affect persistence. */ }
    }
    public void Started() => Write("app.start", new { executable = Environment.ProcessPath, version = typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        moduleId = typeof(App).Assembly.ManifestModule.ModuleVersionId, baseDirectory = AppContext.BaseDirectory });
    public void Window(string stage, Window window) => Write(stage, new { visible = window.IsVisible, visibility = window.Visibility.ToString(), state = window.WindowState.ToString(),
        window.IsActive, window.ShowInTaskbar, window.Topmost, ownerType = window.Owner?.GetType().Name, left = Finite(window.Left), top = Finite(window.Top),
        width = Finite(window.ActualWidth), height = Finite(window.ActualHeight), dpiX = System.Windows.Media.VisualTreeHelper.GetDpi(window).DpiScaleX });
    private static double? Finite(double value) => double.IsFinite(value) ? value : null;
}
