using System.Windows;
using System.Windows.Media.Imaging;

namespace ChihDesktop;

public static class AppBranding
{
    private static readonly Lazy<BitmapFrame> icon = new(() =>
    {
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/ChihDesktop;component/Assets/Branding/chih-icon.ico"))!.Stream;
        var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.MaxBy(f => f.PixelWidth)!;
        frame.Freeze(); return frame;
    });
    public static void Apply(Window window) => window.Icon = icon.Value;
}
