using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ChihDesktop;

// Native coordinates are physical pixels; WPF sizes and speeds are device-independent units.
public sealed class WindowPositionService(Window window)
{
    private nint Handle => new WindowInteropHelper(window).Handle;
    private double fractionalX;

    public void Reset()
    {
        var primary = MonitorFromPoint(new NativePoint(), 1);
        var area = WorkArea(primary);
        GetWindowRect(Handle, out var rect);
        MoveTo(area.Left + (area.Right - area.Left - rect.Width) / 2, area.Bottom - rect.Height);
        Clamp();
    }

    public bool Walk(double distance)
    {
        GetWindowRect(Handle, out var rect);
        fractionalX += distance * VisualTreeHelper.GetDpi(window).DpiScaleX;
        int delta = (int)fractionalX;
        fractionalX -= delta;
        var area = WorkArea(MonitorFromWindow(Handle, 2));
        int x = Math.Clamp(rect.Left + delta, area.Left, Math.Max(area.Left, area.Right - rect.Width));
        MoveTo(x, Math.Clamp(rect.Top, area.Top, Math.Max(area.Top, area.Bottom - rect.Height)));
        bool boundary = distance < 0 ? x <= area.Left : x >= area.Right - rect.Width;
        if (boundary) fractionalX = 0;
        return boundary;
    }

    public void FitToWorkArea()
    {
        var area = WorkArea(MonitorFromWindow(Handle, 2));
        var dpi = VisualTreeHelper.GetDpi(window);
        double width = (area.Right - area.Left) / dpi.DpiScaleX;
        double height = (area.Bottom - area.Top) / dpi.DpiScaleY;
        window.MinWidth = Math.Min(window.MinWidth, width);
        window.MinHeight = Math.Min(window.MinHeight, height);
        window.Width = Math.Min(window.ActualWidth, width);
        window.Height = Math.Min(window.ActualHeight, height);
        window.UpdateLayout();
        Clamp();
    }

    public void Clamp()
    {
        GetWindowRect(Handle, out var rect);
        var area = WorkArea(MonitorFromWindow(Handle, 2));
        MoveTo(Math.Clamp(rect.Left, area.Left, Math.Max(area.Left, area.Right - rect.Width)),
            Math.Clamp(rect.Top, area.Top, Math.Max(area.Top, area.Bottom - rect.Height)));
    }

    public NativePoint Position() { GetWindowRect(Handle, out var r); return new() { X = r.Left, Y = r.Top }; }
    public static NativePoint Cursor() { GetCursorPos(out var p); return p; }
    public void MoveTo(int x, int y) => SetWindowPos(Handle, 0, x, y, 0, 0, 0x0001 | 0x0004 | 0x0010);
    private static NativeRect WorkArea(nint monitor)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfo(monitor, ref info);
        return info.Work;
    }
    [StructLayout(LayoutKind.Sequential)] public struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect
    { public int Left, Top, Right, Bottom; public readonly int Width => Right - Left; public readonly int Height => Bottom - Top; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
}
