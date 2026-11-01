using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using ChihDesktop;

namespace ChihDesktop.Mac;

internal static class PrototypeChecks
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool condition, string name) { if (!condition) throw new InvalidDataException(name); checks++; }
        try
        {
            using var baseline = JsonDocument.Parse(PrototypeModel.Resource("Sprites.baseline.json"));
            var expected = baseline.RootElement.EnumerateArray().ToDictionary(x => x.GetProperty("File").GetString()!, x => x.GetProperty("SHA256").GetString()!);
            Check(expected.Count == 15 && PrototypeModel.Clips.Sum(c => c.Count) == 15, "Exact 15-frame catalog");
            foreach (var clip in PrototypeModel.Clips)
            {
                for (int i = 0; i < clip.Count; i++)
                {
                    string file = $"{clip.Name}-{i:00}.png";
                    using var stream = PrototypeModel.Resource("Sprites." + file); using var data = new MemoryStream(); stream.CopyTo(data);
                    var bytes = data.ToArray();
                    Check(bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "PNG signature " + file);
                    Check(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)) == 320 && BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)) == 260 && bytes[25] == 6, "RGBA dimensions " + file);
                    Check(Convert.ToHexString(SHA256.HashData(bytes)) == expected[file], "Unchanged embedded bytes " + file);
                }
                for (int tick = 0; tick < 1000; tick++)
                    Check(PrototypeModel.Frame(clip.State, tick / 30d) is var index && index >= 0 && index < clip.Count, "Frame bounds " + clip.Name);
            }
            Check(PrototypeModel.Frame(CatState.Walking, .5) == 0, "Four-frame walk at 8 FPS");
            Check(PrototypeModel.Frame(CatState.Stretching, 100) == 2, "Stretch holds final frame");
            Check(PrototypeModel.Clamp(-3000, -1920, 1920, 160) == -1920, "Negative monitor origin");
            Check(PrototypeModel.Clamp(0, -1920, 1920, 160) == -160, "Right boundary");
            Check(PrototypeModel.Clamp(500, 10, 80, 160) == 10, "Small display");
            var behavior = new CatBehaviorService(); behavior.Sleep(); behavior.Update(100);
            Check(behavior.State == CatState.Sleeping, "Shared forced sleep"); behavior.React();
            Check(behavior.State == CatState.Reacting, "Shared click reaction"); behavior.WakeUp();
            Check(behavior.State == CatState.Stretching, "Shared wake");
            using var icon = PrototypeModel.Resource("Branding.chih-icon.png"); Check(icon.Length > 0, "Approved icon embedded");
            Console.WriteLine($"PASS: {checks} prototype asset/model checks (no UI, persistence, user data or SpriteBuilder).");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex.Message); return 1; }
    }
}
