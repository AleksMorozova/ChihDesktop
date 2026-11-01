using System.Reflection;
using ChihDesktop;

namespace ChihDesktop.Mac;

internal static class PrototypeModel
{
    // WPF's ChihArtwork also contains WPF vector drawing code and cannot be linked.
    // This small rendering adapter mirrors its six existing clip descriptors only.
    public static readonly (CatState State, string Name, int Count)[] Clips =
    [
        (CatState.Idle, "Idle", 2), (CatState.Walking, "WalkRight", 4),
        (CatState.Sitting, "Sit", 2), (CatState.Sleeping, "Sleep", 2),
        (CatState.Stretching, "Stretch", 3), (CatState.Reacting, "React", 2)
    ];

    public static Stream Resource(string name) => Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
        ?? throw new InvalidDataException("Missing prototype resource: " + name);

    public static int Frame(CatState state, double seconds) => state switch
    {
        CatState.Walking => (int)(seconds * 8) % 4,
        CatState.Idle => seconds % 5 > 4.8 ? 1 : 0,
        CatState.Sitting or CatState.Sleeping => (int)(seconds * 1.5) % 2,
        CatState.Stretching => Math.Min((int)(seconds * 2), 2),
        CatState.Reacting => (int)(seconds * 4) % 2,
        _ => 0
    };

    public static double Clamp(double value, double start, double length, double petLength) =>
        Math.Clamp(value, start, Math.Max(start, start + length - petLength));
}
