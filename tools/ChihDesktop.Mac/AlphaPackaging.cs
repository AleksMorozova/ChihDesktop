using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace ChihDesktop.Mac;

// Portable packaging checks, not GUI tests. Mac-native tools perform additional validation.
internal static class AlphaPackaging
{
    private static readonly string[] Required = ["ChihDesktop.Mac.dll", "ChihDesktop.Mac.deps.json", "ChihDesktop.Mac.runtimeconfig.json",
        "ChihDesktop.Core.dll", "libhostfxr.dylib", "libhostpolicy.dylib", "libcoreclr.dylib", "libAvaloniaNative.dylib", "libSkiaSharp.dylib"];
    private sealed record Entry(string Path, string Sha256);

    public static int Run(string[] args)
    {
        try
        {
            switch (args)
            {
                case ["--alpha-check-publish", var folder, var rid]: ValidatePublish(folder, rid); break;
                case ["--alpha-check-bundle", var app, var publish, var rid, var version, var build]:
                    ValidateBundle(app, publish, rid, version, build); break;
                case ["--alpha-check-bundle", var app, var publish, var rid, var version, var build, var manifest]:
                    ValidateBundle(app, publish, rid, version, build, manifest); break;
                case ["--alpha-manifest", var app, var output]: WriteManifest(app, output); break;
                case ["--alpha-notices", var assets, var publish, var rid, var output, var mit]:
                    CollectNotices(assets, publish, rid, output, mit); break;
                case ["--self-test-packaging"]: SelfTest(); return 0;
                default: throw new ArgumentException("Invalid packaging-check arguments.");
            }
            Console.WriteLine("PASS: " + args[0]); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex.Message); return 1; }
    }

    private static void Require(bool ok, string reason) { if (!ok) throw new InvalidDataException(reason); }
    private static uint Cpu(string rid) => rid switch { "osx-arm64" => 0x0100000c, "osx-x64" => 0x01000007, _ => throw new ArgumentException("Unsupported RID") };
    internal static bool HasCpu(ReadOnlySpan<byte> data, uint cpu)
    {
        if (data.Length < 8) return false;
        if (BinaryPrimitives.ReadUInt32LittleEndian(data) == 0xfeedfacf)
            return data.Length >= 32 && BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) == cpu;
        uint magic = BinaryPrimitives.ReadUInt32BigEndian(data);
        if (magic is not (0xcafebabe or 0xcafebabf)) return false;
        uint count = BinaryPrimitives.ReadUInt32BigEndian(data[4..]); int stride = magic == 0xcafebabe ? 20 : 32;
        if (count > 64 || 8L + count * stride > data.Length) return false;
        for (int i = 0; i < count; i++) if (BinaryPrimitives.ReadUInt32BigEndian(data[(8 + i * stride)..]) == cpu) return true;
        return false;
    }
    private static void CheckCpu(string file, string rid) => Require(HasCpu(File.ReadAllBytes(file), Cpu(rid)), "Missing target Mach-O architecture: " + Path.GetFileName(file));

    private static IEnumerable<string> Files(string folder)
    {
        Require(Directory.Exists(folder), "Directory missing: " + folder);
        Require((File.GetAttributes(folder) & FileAttributes.ReparsePoint) == 0, "Symlink directory rejected");
        foreach (string entry in Directory.EnumerateFileSystemEntries(folder))
        {
            var attributes = File.GetAttributes(entry);
            Require((attributes & FileAttributes.ReparsePoint) == 0, "Symlink/reparse point rejected: " + entry);
            if ((attributes & FileAttributes.Directory) != 0) { foreach (var file in Files(entry)) yield return file; }
            else yield return entry;
        }
    }
    private static string Relative(string root, string file) => Path.GetRelativePath(root, file).Replace('\\', '/');
    private static string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
    private static void Privacy(string folder)
    {
        foreach (string file in Files(folder))
            Require(!Regex.IsMatch(Relative(folder, file), @"(?i)(^|/)(references|backups|logs|node_modules|testoutput|\.git|\.aws|\.codex)(/|$)|\.(jpg|jpeg|log|bak|pdb|map|pfx|p12|pem|key|db|sqlite|sqlite3)(-(wal|shm|journal))?$|(^|/)(reminders[^/]*|user-settings[^/]*|appsettings[^/]*|pet-settings)\.json$|(^|/)\.env($|\.)|\.tmp-|\.invalid-|spritebuilder|testhost|xunit"),
                "Private/development payload file rejected: " + Relative(folder, file));
    }

    public static void ValidatePublish(string folder, string rid, string? host = "ChihDesktop.Mac")
    {
        Privacy(folder); _ = Cpu(rid);
        foreach (var name in host is null ? Required : Required.Append(host)) Require(File.Exists(Path.Combine(folder, name)), "Missing runtime dependency: " + name);
        if (host is not null) CheckCpu(Path.Combine(folder, host), rid);
        foreach (var lib in Files(folder).Where(f => f.EndsWith(".dylib", StringComparison.Ordinal))) CheckCpu(lib, rid);
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "ChihDesktop.Mac.runtimeconfig.json")));
        var options = config.RootElement.GetProperty("runtimeOptions");
        Require(!options.TryGetProperty("framework", out _) && !options.TryGetProperty("frameworks", out _) &&
            options.GetProperty("includedFrameworks").EnumerateArray().Any(f => f.GetProperty("name").GetString() == "Microsoft.NETCore.App"), "Payload must be self-contained");
        string deps = File.ReadAllText(Path.Combine(folder, "ChihDesktop.Mac.deps.json"));
        Require(!Regex.IsMatch(deps, "WebView2|PresentationFramework|ChihDesktop.Infrastructure"), "Unexpected Windows/infrastructure dependency");
    }
    internal static void CheckPlist(string text, string version, string build)
    {
        Require(Regex.IsMatch(version, @"^\d+\.\d+\.\d+$") && Regex.IsMatch(build, @"^\d+(\.\d+){0,2}$"), "Invalid alpha version/build");
        using var input = new StringReader(text);
        using var reader = XmlReader.Create(input, new() { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
        var elements = XDocument.Load(reader).Element("plist")?.Element("dict")?.Elements().ToArray() ?? throw new InvalidDataException("Invalid plist root");
        Require(elements.Length % 2 == 0, "Malformed plist dictionary");
        var values = new Dictionary<string, XElement>();
        for (int i = 0; i < elements.Length; i += 2)
        { Require(elements[i].Name == "key" && values.TryAdd(elements[i].Value, elements[i + 1]), "Duplicate/invalid plist key"); }
        void Value(string key, string expected) => Require(values.TryGetValue(key, out var value) && value.Name == "string" && value.Value == expected, "Incorrect plist: " + key);
        Value("CFBundleExecutable", "ChihDesktop"); Value("CFBundleIdentifier", "org.chihdesktop.alpha");
        Value("CFBundleName", "ChihDesktop"); Value("CFBundleDisplayName", "ChihDesktop Alpha");
        Value("CFBundlePackageType", "APPL"); Value("CFBundleIconFile", "chih-icon.icns");
        Value("CFBundleShortVersionString", version); Value("CFBundleVersion", build); Value("LSMinimumSystemVersion", "14.0");
        Require(values.TryGetValue("NSHighResolutionCapable", out var retina) && retina.Name == "true", "Retina support required");
        Require(values.TryGetValue("LSUIElement", out var agent) && agent.Name == "false", "Keep Dock/Quit recovery for alpha");
    }
    internal static bool IsIcns(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16 || !data[..4].SequenceEqual("icns"u8) || BinaryPrimitives.ReadUInt32BigEndian(data[4..]) != data.Length) return false;
        int offset = 8, images = 0;
        while (offset < data.Length)
        {
            if (data.Length - offset < 8) return false;
            uint length = BinaryPrimitives.ReadUInt32BigEndian(data[(offset + 4)..]);
            if (length <= 8 || length > data.Length - offset) return false;
            string type = System.Text.Encoding.ASCII.GetString(data.Slice(offset, 4));
            if (type is "icp4" or "icp5" or "icp6" or "ic07" or "ic08" or "ic09" or "ic10" or "ic11" or "ic12" or "ic13" or "ic14") images++;
            offset += (int)length;
        }
        return offset == data.Length && images > 0;
    }

    public static void ValidateBundle(string app, string publish, string rid, string version, string build, string? signedManifest = null)
    {
        Require(Path.GetFileName(Path.TrimEndingDirectorySeparator(app)) == "ChihDesktop.app", "Unexpected bundle name");
        Privacy(app); string mac = Path.Combine(app, "Contents/MacOS");
        string payload = Path.Combine(app, "Contents/Resources/runtime");
        CheckBundleHost(mac, rid);
        ValidatePublish(payload, rid, null);
        CheckPlist(File.ReadAllText(Path.Combine(app, "Contents/Info.plist")), version, build);
        Require(IsIcns(File.ReadAllBytes(Path.Combine(app, "Contents/Resources/chih-icon.icns"))), "Invalid ICNS container");
        CheckPublishFiles(publish, payload, rid, signedManifest is not null);
        if (!OperatingSystem.IsWindows())
            Require((File.GetUnixFileMode(Path.Combine(mac, "ChihDesktop")) & UnixFileMode.UserExecute) != 0, "Bundle executable lacks execute permission");
        if (signedManifest is not null)
        {
            Require(File.Exists(Path.Combine(app, "Contents/_CodeSignature/CodeResources")), "Signed bundle resource seal missing");
            CheckManifest(app, signedManifest);
        }
    }
    private static void CheckBundleHost(string mac, string rid)
    {
        string host = Path.Combine(mac, "ChihDesktop");
        Require(Directory.EnumerateFileSystemEntries(mac).Count() == 1 && File.Exists(host), "MacOS must contain only the native apphost, not managed assemblies");
        CheckCpu(host, rid);
        Require(File.ReadAllBytes(host).AsSpan().IndexOf("../Resources/runtime/ChihDesktop.Mac.dll\0"u8) >= 0,
            "Bundle apphost does not point to the self-contained Resources/runtime payload");
    }
    private static void CheckPublishFiles(string publish, string mac, string rid, bool signed)
    {
        foreach (var file in Files(publish))
        {
            string relative = Relative(publish, file);
            // The bundle's apphost is generated separately by the SDK with a relative
            // managed entry path; every other publish file retains its payload path.
            if (relative == "ChihDesktop.Mac") continue;
            string target = Path.Combine(mac, relative);
            Require(File.Exists(target), "Publish file missing in bundle: " + relative);
            // codesign changes Mach-O bytes. Compare the original bytes BEFORE signing;
            // afterwards native codesign + the external final manifest protect those files.
            if (signed && HasCpu(File.ReadAllBytes(file), Cpu(rid))) CheckCpu(target, rid);
            else Require(Hash(file) == Hash(target), "Publish file changed in bundle: " + relative);
        }
    }
    private static Entry[] Manifest(string app) => Files(app)
        .Select(f => new Entry(Relative(app, f), Hash(f))).OrderBy(f => f.Path, StringComparer.Ordinal).ToArray();
    private static void RequireExternalManifest(string app, string output)
    {
        string relative = Path.GetRelativePath(Path.GetFullPath(app), Path.GetFullPath(output));
        Require(relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative),
            "Manifest must remain outside the signed bundle");
    }
    private static void WriteManifest(string app, string output)
    {
        RequireExternalManifest(app, output);
        Require(!File.Exists(output), "Refusing to overwrite manifest");
        File.WriteAllText(output, JsonSerializer.Serialize(Manifest(app), new JsonSerializerOptions { WriteIndented = true }));
    }
    private static void CheckManifest(string app, string input)
    {
        RequireExternalManifest(app, input);
        var expected = JsonSerializer.Deserialize<Entry[]>(File.ReadAllText(input)) ?? [];
        Require(expected.Length > 0 && expected.SequenceEqual(Manifest(app)), "Signed bundle manifest mismatch");
    }

    private static void CollectNotices(string assetsFile, string publish, string rid, string output, string projectMit)
    {
        using var assets = JsonDocument.Parse(File.ReadAllText(assetsFile));
        string[] roots = assets.RootElement.GetProperty("packageFolders").EnumerateObject().Select(p => p.Name).ToArray();
        string sourceMit = File.ReadAllText(projectMit);
        int grant = sourceMit.IndexOf("Permission is hereby granted", StringComparison.Ordinal);
        Require(grant >= 0, "Standard MIT grant missing");
        Directory.CreateDirectory(output); var records = new List<object>();
        foreach (var package in assets.RootElement.GetProperty("libraries").EnumerateObject())
        {
            if (package.Value.GetProperty("type").GetString() != "package") continue;
            string relative = package.Value.GetProperty("path").GetString()!;
            string? directory = roots.Select(r => Path.Combine(r, relative)).FirstOrDefault(Directory.Exists);
            Require(directory is not null, "NuGet cache entry missing: " + package.Name);
            string spec = Directory.EnumerateFiles(directory!, "*.nuspec").Single();
            using var reader = XmlReader.Create(spec, new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var metadata = XDocument.Load(reader).Descendants().First(e => e.Name.LocalName == "metadata");
            string Read(string name) => metadata.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value ?? "";
            string license = Read("license"), copyright = Read("copyright");
            string dest = Path.Combine(output, package.Name.Replace('/', '-')); Directory.CreateDirectory(dest);
            int copies = CopyLicenseFiles(directory!, dest);
            if (license == "MIT") File.WriteAllText(Path.Combine(dest, "MIT.txt"), copyright + "\n\n" + sourceMit[grant..]);
            Require(license == "MIT" || copies > 0, "License review required: " + package.Name);
            records.Add(new { Package = package.Name, License = license, Copyright = copyright, CopiedNotices = copies });
        }
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(publish, "ChihDesktop.Mac.runtimeconfig.json")));
        foreach (var framework in config.RootElement.GetProperty("runtimeOptions").GetProperty("includedFrameworks").EnumerateArray())
        {
            string name = framework.GetProperty("name").GetString()! + ".Runtime." + rid;
            string version = framework.GetProperty("version").GetString()!;
            var locations = roots.Select(r => Path.Combine(r, name.ToLowerInvariant(), version)).ToList();
            if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { } dotnet) locations.Add(Path.Combine(dotnet, "packs", name, version));
            string? location = locations.FirstOrDefault(Directory.Exists);
            Require(location is not null, "Runtime-pack notices missing: " + name);
            string dest = Path.Combine(output, name + "-" + version); Directory.CreateDirectory(dest);
            Require(CopyLicenseFiles(location!, dest) > 0, "Runtime license files missing: " + name);
            records.Add(new { Package = name + "/" + version, License = "See bundled runtime notices" });
        }
        File.WriteAllText(Path.Combine(output, "dependency-inventory.json"), JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static int CopyLicenseFiles(string source, string dest)
    {
        int count = 0;
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            if (!Regex.IsMatch(Path.GetFileName(file), @"(?i)^(licen[sc]e|notice|third[-_]?party[-_]?notices?)(\.[a-z]+)?$")) continue;
            Require((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0, "Symlinked license file rejected");
            string relative = Path.GetRelativePath(source, file);
            string target = Path.Combine(dest, relative); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, false); count++;
        }
        return count;
    }

    private static void SelfTest()
    {
        int count = 0;
        void Test(bool ok) { Require(ok, "Packaging self-check failed: " + count); count++; }
        var header = new byte[32]; BinaryPrimitives.WriteUInt32LittleEndian(header, 0xfeedfacf); BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), Cpu("osx-arm64"));
        Test(HasCpu(header, Cpu("osx-arm64"))); Test(!HasCpu(header, Cpu("osx-x64"))); Test(!HasCpu([], Cpu("osx-arm64")));
        var fat = new byte[28]; BinaryPrimitives.WriteUInt32BigEndian(fat, 0xcafebabe); BinaryPrimitives.WriteUInt32BigEndian(fat.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(fat.AsSpan(8), Cpu("osx-x64")); Test(HasCpu(fat, Cpu("osx-x64"))); Test(!HasCpu(fat, Cpu("osx-arm64")));
        Test(!IsIcns("not an icon"u8)); Test(!IsIcns(new byte[16]));
        string plist = "<plist><dict>" + string.Join("", new Dictionary<string, string> {
            ["CFBundleExecutable"]="ChihDesktop", ["CFBundleIdentifier"]="org.chihdesktop.alpha", ["CFBundleName"]="ChihDesktop",
            ["CFBundleDisplayName"]="ChihDesktop Alpha", ["CFBundlePackageType"]="APPL", ["CFBundleIconFile"]="chih-icon.icns",
            ["CFBundleShortVersionString"]="0.4.0", ["CFBundleVersion"]="1.1", ["LSMinimumSystemVersion"]="14.0"
        }.Select(p => $"<key>{p.Key}</key><string>{p.Value}</string>")) + "<key>NSHighResolutionCapable</key><true/><key>LSUIElement</key><false/></dict></plist>";
        CheckPlist(plist, "0.4.0", "1.1"); Test(true);
        foreach (string key in new[] { "CFBundleExecutable", "CFBundleIdentifier", "CFBundleIconFile", "LSMinimumSystemVersion", "CFBundleVersion" })
        {
            bool rejected = false; try { CheckPlist(plist.Replace("<key>" + key, "<key>Wrong" + key), "0.4.0", "1.1"); } catch (InvalidDataException) { rejected = true; }
            Test(rejected);
        }
        bool duplicate = false; try { CheckPlist(plist.Replace("</dict>", "<key>CFBundleExecutable</key><string>Other</string></dict>"), "0.4.0", "1.1"); } catch (InvalidDataException) { duplicate = true; }
        Test(duplicate);
        var temp = Directory.CreateTempSubdirectory("ChihDesktop-signing-tests-");
        try
        {
            string app = Path.Combine(temp.FullName, "ChihDesktop.app"), publish = Path.Combine(temp.FullName, "publish");
            string mac = Path.Combine(app, "Contents/MacOS"), seal = Path.Combine(app, "Contents/_CodeSignature");
            Directory.CreateDirectory(mac); Directory.CreateDirectory(seal); Directory.CreateDirectory(publish);
            File.WriteAllBytes(Path.Combine(publish, "fixture.dylib"), header);
            File.WriteAllBytes(Path.Combine(mac, "fixture.dylib"), header);
            File.WriteAllText(Path.Combine(publish, "managed.dll"), "synthetic managed payload");
            File.Copy(Path.Combine(publish, "managed.dll"), Path.Combine(mac, "managed.dll"));
            File.WriteAllText(Path.Combine(seal, "CodeResources"), "synthetic seal, not a real signature");
            void Reject(Action action) { bool rejected = false; try { action(); } catch (InvalidDataException) { rejected = true; } Test(rejected); }
            CheckPublishFiles(publish, mac, "osx-arm64", false); Test(true);
            File.AppendAllText(Path.Combine(mac, "fixture.dylib"), "synthetic signature bytes");
            Reject(() => CheckPublishFiles(publish, mac, "osx-arm64", false));
            CheckPublishFiles(publish, mac, "osx-arm64", true); Test(true);
            string manifest = Path.Combine(temp.FullName, "signed-manifest.json");
            Reject(() => WriteManifest(app, Path.Combine(app, "manifest.json")));
            WriteManifest(app, manifest); CheckManifest(app, manifest); Test(true);
            File.AppendAllText(Path.Combine(mac, "managed.dll"), "tampered");
            Reject(() => CheckPublishFiles(publish, mac, "osx-arm64", true));
            Reject(() => CheckManifest(app, manifest));
            File.Copy(Path.Combine(publish, "managed.dll"), Path.Combine(mac, "managed.dll"), true);
            File.AppendAllText(Path.Combine(mac, "fixture.dylib"), "post-sign mutation");
            Reject(() => CheckManifest(app, manifest));
            string hostFolder = Path.Combine(temp.FullName, "host-only"); Directory.CreateDirectory(hostFolder);
            string host = Path.Combine(hostFolder, "ChihDesktop");
            File.WriteAllBytes(host, [.. header, .. "../Resources/runtime/ChihDesktop.Mac.dll\0"u8]);
            CheckBundleHost(hostFolder, "osx-arm64"); Test(true);
            File.WriteAllText(Path.Combine(hostFolder, "Microsoft.CSharp.dll"), "synthetic PE payload");
            Reject(() => CheckBundleHost(hostFolder, "osx-arm64"));
            File.Delete(Path.Combine(hostFolder, "Microsoft.CSharp.dll"));
            File.WriteAllBytes(host, header);
            Reject(() => CheckBundleHost(hostFolder, "osx-arm64"));
            File.Delete(Path.Combine(seal, "CodeResources"));
            Reject(() => CheckManifest(app, manifest));
        }
        finally { temp.Delete(true); }
        Console.WriteLine($"PASS: {count} portable packaging parser/negative checks (not macOS tool or GUI validation).");
    }
}
