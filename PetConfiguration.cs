using System.IO;
using System.Text.Json;

namespace ChihDesktop;

public sealed class PetConfiguration
{
    public double CatSize { get; set; } = 160;
    public double WalkingSpeed { get; set; } = 70;
    public int AnimationFps { get; set; } = 30;
    public bool AlwaysOnTop { get; set; } = true;

    public static PetConfiguration From(UserSettings settings) => new() { CatSize = settings.CatSize, WalkingSpeed = settings.WalkingSpeed, AnimationFps = settings.AnimationFps, AlwaysOnTop = settings.PetTopmost };

    // Read-only compatibility import; never writes the legacy file beside the executable.
    public static PetConfiguration Load(string? legacyPath = null)
    {
        PetConfiguration config;
        try
        {
            config = JsonSerializer.Deserialize<PetConfiguration>(
                File.ReadAllText(legacyPath ?? Path.Combine(AppContext.BaseDirectory, "appsettings.json")),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { config = new(); }
        config.CatSize = double.IsFinite(config.CatSize) ? Math.Clamp(config.CatSize, 64, 400) : 160;
        config.WalkingSpeed = double.IsFinite(config.WalkingSpeed) ? Math.Clamp(config.WalkingSpeed, 10, 500) : 70;
        config.AnimationFps = Math.Clamp(config.AnimationFps, 5, 60);
        return config;
    }
}
