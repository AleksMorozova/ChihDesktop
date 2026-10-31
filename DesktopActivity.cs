using System.Runtime.InteropServices;
using System.Text;

namespace ChihDesktop;

public static class DesktopActivity
{
    public static TimeSpan IdleTime()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        return GetLastInputInfo(ref info) ? TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount64 - info.Time)) : TimeSpan.Zero;
    }

    public static bool IsFullscreenApp()
    {
        nint foreground = GetForegroundWindow();
        if (foreground == 0 || foreground == GetShellWindow() || foreground == GetDesktopWindow() || IsIconic(foreground)) return false;
        GetWindowThreadProcessId(foreground, out uint process);
        if (process == Environment.ProcessId) return false;
        var name = new StringBuilder(128);
        GetClassName(foreground, name, name.Capacity);
        if (name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        if (!GetWindowRect(foreground, out var rect)) return false;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(foreground, 2), ref info)) return false;
        return rect.Left <= info.Monitor.Left + 1 && rect.Top <= info.Monitor.Top + 1 &&
            rect.Right >= info.Monitor.Right - 1 && rect.Bottom >= info.Monitor.Bottom - 1;
    }

    [StructLayout(LayoutKind.Sequential)] private struct LastInputInfo { public uint Size, Time; }
    [StructLayout(LayoutKind.Sequential)] private struct RectI { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public RectI Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInputInfo info);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetShellWindow();
    [DllImport("user32.dll")] private static extern nint GetDesktopWindow();
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder name, int count);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out RectI rect);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
