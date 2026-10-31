using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ChihDesktop;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Geometry strings must use a dot decimal separator on every developer machine.
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        if (args.Length > 1 && args[0] == "--verify-app") return VerifyApplication(args[1]);
        string workspace = SpriteOutputPolicy.FindWorkspace(AppContext.BaseDirectory);
        string output = SpriteOutputPolicy.Resolve(workspace, args.Length > 0 ? args[0] : null);
        Directory.CreateDirectory(output);
        SpriteOutputPolicy.Resolve(workspace, output);
        int count = 0;
        foreach (var clip in ChihArtwork.Clips)
        for (int i = 0; i < clip.Count; i++)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(2, 2));
                dc.DrawDrawing(ChihArtwork.Draw(clip.State, i));
                dc.Pop();
            }
            var bitmap = new RenderTargetBitmap(320, 260, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            string path = Path.Combine(output, $"{clip.Name}-{i:00}.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = SpriteOutputPolicy.CreateFile(workspace, output, Path.GetFileName(path))) encoder.Save(stream);
            Validate(path);
            count++;
        }
        Console.WriteLine($"Generated and validated {count} vector fallback PNGs: 320x260, real alpha, transparent border, shared ground baseline. No atlas crops or labels used.");
        return 0;
    }
    private static void Validate(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var bitmap = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        if (bitmap.PixelWidth != 320 || bitmap.PixelHeight != 260) throw new InvalidDataException(path);
        var pixels = new byte[320 * 260 * 4];
        bitmap.CopyPixels(pixels, 320 * 4, 0);
        int transparent = 0, opaque = 0, partial = 0, bottom = 0;
        for (int y = 0; y < 260; y++)
        for (int x = 0; x < 320; x++)
        {
            byte alpha = pixels[(y * 320 + x) * 4 + 3];
            if (alpha == 0) transparent++;
            else { bottom = Math.Max(bottom, y); if (alpha == 255) opaque++; else partial++; }
            if ((x < 3 || x >= 317 || y < 3 || y >= 257) && alpha != 0)
                throw new InvalidDataException($"Clipped artwork: {path}");
        }
        if (transparent < 40000 || opaque == 0 || partial == 0 || bottom != 244)
            throw new InvalidDataException($"Alpha or baseline validation failed: {path}; bottom={bottom}");
        Console.WriteLine($"{Path.GetFileName(path)}: transparent={transparent}, opaque={opaque}, edge alpha={partial}, bottom={bottom}");
    }

    // Exercise the built application's real resource loading and renderer without opening a window.
    private static int VerifyApplication(string assemblyPath)
    {
        var assembly = System.Reflection.Assembly.LoadFrom(Path.GetFullPath(assemblyPath));
        _ = new Application();
        var rendererType = assembly.GetType("ChihDesktop.AnimationService", true)!;
        var update = rendererType.GetMethod("Update")!;
        var stateType = update.GetParameters()[0].ParameterType;
        var directionType = update.GetParameters()[1].ParameterType;
        var renderer = (FrameworkElement)Activator.CreateInstance(rendererType)!;
        renderer.Measure(new Size(160, 130));
        renderer.Arrange(new Rect(0, 0, 160, 130));
        renderer.UpdateLayout();
        int tested = 0;
        var preview = new DrawingVisual();
        using (var dc = preview.RenderOpen())
        {
            foreach (var clip in ChihArtwork.Clips)
            foreach (string direction in new[] { "Right", "Left" })
            {
                // Reset animation phase by entering a different state first.
                var other = clip.State == CatState.Idle ? CatState.Sleeping : CatState.Idle;
                update.Invoke(renderer, [Enum.Parse(stateType, other.ToString()), Enum.Parse(directionType, direction), 0d]);
                for (int i = 0; i < clip.Count; i++)
                {
                    double dt = i == 0 ? 0 : clip.State switch
                    {
                        CatState.Idle => 4.9, CatState.Walking => .125,
                        CatState.Sleeping or CatState.Sitting => .67,
                        CatState.Stretching => .5, _ => .25
                    };
                    update.Invoke(renderer, [Enum.Parse(stateType, clip.State.ToString()), Enum.Parse(directionType, direction), dt]);
                    renderer.UpdateLayout();
                    var bitmap = new RenderTargetBitmap(320, 260, 192, 192, PixelFormats.Pbgra32);
                    bitmap.Render(renderer);
                    byte[] pixels = new byte[320 * 260 * 4];
                    bitmap.CopyPixels(pixels, 1280, 0);
                    if (!pixels.Where((_, index) => index % 4 == 3).Any(alpha => alpha == 255))
                        throw new InvalidDataException($"Empty application render: {clip.Name}/{direction}/{i}");
                    dc.DrawImage(bitmap, new Rect((tested % 6) * 160, (tested / 6) * 130, 160, 130));
                    tested++;
                }
            }
        }
        var sheet = new RenderTargetBitmap(960, 650, 96, 96, PixelFormats.Pbgra32);
        sheet.Render(preview);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(sheet));
        string output = SpriteOutputPolicy.Resolve(SpriteOutputPolicy.FindWorkspace(AppContext.BaseDirectory), null);
        Directory.CreateDirectory(output);
        SpriteOutputPolicy.Resolve(SpriteOutputPolicy.FindWorkspace(AppContext.BaseDirectory), output);
        string previewName = "renderer-preview-" + Guid.NewGuid().ToString("N") + ".png";
        string previewPath = Path.Combine(output, previewName);
        using (var file = SpriteOutputPolicy.CreateFile(SpriteOutputPolicy.FindWorkspace(AppContext.BaseDirectory), output, previewName)) encoder.Save(file);
        Console.WriteLine($"Application renderer verified: {tested} frames/directions, all embedded PNG paths loaded; diagnostic preview: {previewPath}");
        return 0;
    }
}
