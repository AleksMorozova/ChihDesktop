using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text.Json;
using ChihDesktop;
using Xunit;

namespace ChihDesktop.Tests;

public sealed class PersistenceRegressionTests
{
    [Fact]
    public void Persistence_and_artwork_workflow_preserves_all_94_assertions()
    {
        // All persistence fixtures use synthetic titles/IDs. No photos, credentials or user profile files.
        string workspace = SpriteOutputPolicy.FindWorkspace(AppContext.BaseDirectory);
        string folder = Path.Combine(workspace, "tools", "PersistenceChecks", "TestOutput", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        int count = 0;
        void Check(bool condition, string name)
        { Assert.True(condition, name); count++; Console.WriteLine("PASS " + name); }
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        ReminderData Fixture() => new()
        {
            QuietHoursEnabled = false,
            Reminders =
            [
                new() { Id = "item-a", Title = "Пункт A", Time = new(14, 0), BubbleGroup = "pair", MessageVariants = ["Тестовый пункт A"] },
                new() { Id = "item-b", Title = "Пункт B", Time = new(14, 0), BubbleGroup = "pair", MessageVariants = ["Тестовый пункт B"] }
            ]
        };
        string Seed(string name, ReminderData? data = null)
        {
            string path = Path.Combine(folder, name + ".json");
            File.WriteAllText(path, JsonSerializer.Serialize(data ?? Fixture(), ReminderStore.JsonOptions));
            return path;
        }
        var now = new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero); // 14:00 Kyiv
        string Snapshot(ReminderCommands commands) => JsonSerializer.Serialize(commands.GetSnapshot(), ReminderStore.JsonOptions);
        var baseline = JsonSerializer.Deserialize<List<SpriteHash>>(File.ReadAllText(Path.Combine(workspace, "tools", "PersistenceChecks", "sprite-baseline.json")))!;
        void VerifySprites(string phase)
        {
            Check(baseline.Count == 15 && Directory.GetFiles(Path.Combine(workspace, "Assets", "Sprites"), "*.png").Length == 15, phase + " production sprite file set");
            foreach (var item in baseline)
                Check(Hash(Path.Combine(workspace, "Assets", "Sprites", item.File)) == item.SHA256, phase + " unchanged " + item.File);
        }
        VerifySprites("Before");

        string recovery = Seed("recovery");
        var recoverData = Fixture();
        recoverData.Occurrences.Add(new() { Id = "saved-state", ReminderId = "item-a", Title = "Пункт A", LocalDate = new(2026, 10, 31),
            DueAtUtc = now, Acknowledgment = ReminderAcknowledgment.Completed, AcknowledgedAtUtc = now, RequiresConfirmation = true });
        string validBackup = JsonSerializer.Serialize(recoverData, ReminderStore.JsonOptions);
        File.WriteAllText(recovery + ".bak", validBackup);
        const string corruptBytes = "{broken-json";
        File.WriteAllText(recovery, corruptBytes);
        var recoveryStore = new ReminderStore(recovery);
        var recovered = new ReminderCommands(recoveryStore);
        Check(recoveryStore.LoadOutcome!.Status == ReminderLoadStatus.RecoveredBackup, "corrupt primary selects validated backup explicitly");
        Check(recovered.GetSnapshot().Occurrences.Single().Acknowledgment == ReminderAcknowledgment.Completed, "recovery retains saved acknowledgment");
        Check(File.ReadAllText(recovery + ".bak") == validBackup, "corrupt primary never rotates over valid backup");
        Check(Directory.GetFiles(folder, "recovery.json.invalid-*").Any(p => File.ReadAllText(p) == corruptBytes), "exact corrupt bytes preserved for diagnostics");
        Check(new ReminderStore(recovery).Load().Status == ReminderLoadStatus.RecoveredBackup && recovered.StartupWarning is not null && File.ReadAllText(recovery) == corruptBytes,
            "recovery stays explicit without rewriting primary on startup");

        string future = Seed("future");
        File.WriteAllText(future + ".bak", validBackup);
        const string futureBytes = "{\"Version\":99,\"DifferentModel\":{\"data\":true}}";
        File.WriteAllText(future, futureBytes);
        var futureStore = new ReminderStore(future);
        var blocked = new ReminderCommands(futureStore);
        Check(futureStore.LoadOutcome!.Status == ReminderLoadStatus.BlockedUnsupported, "future schema recognized before model deserialization");
        Check(!futureStore.Save(Fixture()) && File.ReadAllText(future) == futureBytes && File.ReadAllText(future + ".bak") == validBackup,
            "future primary and valid backup are both protected from downgrade");
        blocked.Tick(now, TimeSpan.Zero);
        Check(blocked.GetSnapshot().Reminders.Count == 0 && blocked.GetSnapshot().Occurrences.Count == 0 && blocked.PersistenceError is not null,
            "unsupported store disables scheduling instead of starting new defaults");
        string unsupportedBackup = Seed("future-backup");
        File.WriteAllText(unsupportedBackup + ".bak", futureBytes);
        var supported = new ReminderCommands(new ReminderStore(unsupportedBackup));
        supported.Tick(now, TimeSpan.Zero);
        Check(File.ReadAllText(unsupportedBackup + ".bak") == futureBytes && supported.GetSnapshot().Occurrences.Count == 2,
            "valid primary usable without overwriting unsupported backup");

        string bothBad = Seed("both-bad");
        File.WriteAllText(bothBad, "[invalid]"); File.WriteAllText(bothBad + ".bak", "{}");
        var badStore = new ReminderStore(bothBad);
        var bad = new ReminderCommands(badStore);
        Check(badStore.LoadOutcome!.Status == ReminderLoadStatus.BlockedCorrupt && !badStore.Save(Fixture()), "no valid copy blocks automatic reset");
        Check(File.ReadAllText(bothBad) == "[invalid]" && File.ReadAllText(bothBad + ".bak") == "{}", "both corrupt originals retained");
        Check(Directory.GetFiles(folder, "both-bad.json*.invalid-*").Length == 2, "both corrupt inputs preserved separately");
        string corruptBackup = Seed("corrupt-backup");
        File.WriteAllText(corruptBackup + ".bak", "not-json");
        var primaryCommands = new ReminderCommands(new ReminderStore(corruptBackup));
        primaryCommands.UpdateSettings(primaryCommands.GetSnapshot().Reminders, true, new(22, 0), new(8, 0));
        Check(new ReminderStore(corruptBackup + ".bak").Load().Status == ReminderLoadStatus.Primary &&
            Directory.GetFiles(folder, "corrupt-backup.json.bak.invalid-*").Length >= 1,
            "corrupt backup is preserved then replaced only by validated primary");
        string bomPath = Seed("bom");
        File.WriteAllText(bomPath, JsonSerializer.Serialize(Fixture(), ReminderStore.JsonOptions), new System.Text.UTF8Encoding(true));
        Check(new ReminderStore(bomPath).Load().Status == ReminderLoadStatus.Primary, "UTF-8 BOM from Windows editors is a valid primary");
        string utf16Path = Seed("utf16");
        File.WriteAllText(utf16Path, JsonSerializer.Serialize(Fixture(), ReminderStore.JsonOptions), System.Text.Encoding.Unicode);
        Check(new ReminderStore(utf16Path).Load().Status == ReminderLoadStatus.Primary, "UTF-16 BOM from Windows editors is a valid primary");
        string backupFutureOnly = Path.Combine(folder, "backup-future-only.json");
        File.WriteAllText(backupFutureOnly + ".bak", futureBytes);
        var futureBackupStore = new ReminderStore(backupFutureOnly);
        Check(futureBackupStore.Load().Status == ReminderLoadStatus.BlockedUnsupported && !futureBackupStore.Save(Fixture()) &&
            !File.Exists(backupFutureOnly) && File.ReadAllText(backupFutureOnly + ".bak") == futureBytes,
            "unsupported backup without primary never becomes a fresh store");
        string duplicatePath = Seed("duplicate");
        var duplicates = Fixture(); duplicates.Reminders.Add(ReminderStore.Copy(duplicates.Reminders[0]));
        File.WriteAllText(duplicatePath, JsonSerializer.Serialize(duplicates, ReminderStore.JsonOptions));
        Check(new ReminderStore(duplicatePath).Load().Status == ReminderLoadStatus.BlockedCorrupt, "schema validation rejects duplicate definition IDs");

        var faultStore = new FailingCommitStore(Seed("failure"));
        var commands = new ReminderCommands(faultStore);
        faultStore.LoadOutcome!.Data!.Reminders.Clear();
        Check(commands.GetSnapshot().Reminders.Count == 2, "public store load result cannot mutate command-owned state");
        commands.Tick(now, TimeSpan.Zero);
        var pair = commands.TakeNextGroup(now)!;
        Check(pair.Count == 2 && pair.All(o => o.Acknowledgment == ReminderAcknowledgment.Pending), "group starts with two independent pending items");
        string stateBefore = Snapshot(commands), diskBefore = File.ReadAllText(faultStore.FilePath);
        int errors = 0; commands.OperationFailed += _ => errors++;
        faultStore.FailPrimaryCommit = true;
        var failedAck = commands.Acknowledge(pair[0].Id, true, now);
        Check(!failedAck.Success && failedAck.Error is not null && errors == 1, "simulated commit failure returns actionable acknowledgment error");
        Check(Snapshot(commands) == stateBefore && commands.GetPresentation().All(o => o.Acknowledgment == ReminderAcknowledgment.Pending),
            "failed checkbox confirmation rolls back published state and current presentation");
        Check(File.ReadAllText(faultStore.FilePath) == diskBefore && new ReminderStore(faultStore.FilePath + ".bak").Load().Data is not null,
            "failed primary commit preserves original primary and a valid backup");
        Check(!commands.Snooze(pair[0].Id, now).Success && Snapshot(commands) == stateBefore, "failed snooze rolls back due and notification state");
        Check(!commands.Acknowledge(pair[0].Id, false, now).Success && Snapshot(commands) == stateBefore, "failed dismiss does not change pending state");
        var edit = commands.GetSnapshot(); edit.Reminders[0].Title = "Изменённый пункт";
        Check(!commands.UpdateSettings(edit.Reminders, true, new(21,0), new(9,0)).Success && Snapshot(commands) == stateBefore,
            "failed settings save rolls back title and quiet hours together");
        var personality = commands.GetPersonalityOptions(); personality.PersonalityEnabled = false;
        Check(!commands.UpdatePersonality(personality).Success && Snapshot(commands) == stateBefore, "failed personality preference save rolls back");
        Check(Directory.GetFiles(folder, "*.tmp-*").Length == 0, "failed atomic commits clean temporary files");
        faultStore.FailPrimaryCommit = false;
        Check(commands.Acknowledge(pair[0].Id, true, now).Success && commands.PersistenceError is null, "retry succeeds after write fault is removed");
        Check(commands.GetSnapshot().Occurrences.Single(o => o.Id == pair[0].Id).Acknowledgment == ReminderAcknowledgment.Completed &&
            commands.GetSnapshot().Occurrences.Single(o => o.Id == pair[1].Id).Acknowledgment == ReminderAcknowledgment.Pending,
            "successful per-item confirmation never confirms sibling");
        faultStore.FailPrimaryCommit = true;
        Check(!commands.Acknowledge(pair[1].Id, true, now).Success && commands.GetSnapshot().Occurrences.Single(o => o.Id == pair[0].Id).Acknowledgment == ReminderAcknowledgment.Completed,
            "failure of sibling acknowledgment preserves earlier durable completion");
        faultStore.FailPrimaryCommit = false;
        var restart = new ReminderCommands(new ReminderStore(faultStore.FilePath));
        Check(restart.GetSnapshot().Occurrences.Single(o => o.Id == pair[0].Id).Acknowledgment == ReminderAcknowledgment.Completed,
            "successful acknowledgment survives restart");
        restart.Tick(now.AddMinutes(1), TimeSpan.Zero);
        Check(restart.GetSnapshot().Occurrences.Count == 2 && restart.TakeNextGroup(now.AddMinutes(1)) is null,
            "restart deduplicates both occurrence creation and presented group");

        var close = new ReminderCommands(new ReminderStore(Seed("close")));
        close.Tick(now, TimeSpan.Zero); close.TakeNextGroup(now);
        close.ClosePresentation([]);
        Check(close.GetPresentation().Count == 2, "closing stale or empty UI cannot clear newly committed group presentation");
        string beforeClose = Snapshot(close), closeFile = File.ReadAllText(close.ConfigurationPath);
        close.ClosePresentation();
        Check(close.GetPresentation().Count == 0 && Snapshot(close) == beforeClose && File.ReadAllText(close.ConfigurationPath) == closeFile,
            "closing presentation does not acknowledge, dismiss or write to disk");
        Check(close.GetSnapshot().Occurrences.All(o => o.Acknowledgment == ReminderAcknowledgment.Pending), "closing bubble leaves both items pending");
        var detached = close.GetSnapshot(); detached.Reminders.Clear(); detached.Occurrences[0].Acknowledgment = ReminderAcknowledgment.Completed;
        Check(close.GetSnapshot().Reminders.Count == 2 && close.GetSnapshot().Occurrences.All(o => o.Acknowledgment == ReminderAcknowledgment.Pending),
            "mutating returned snapshot cannot mutate live scheduler state");
        Check(!close.Acknowledge("unknown-id", true, now).Success, "command boundary rejects unknown occurrence IDs");
        var whitelist = close.GetSnapshot(); whitelist.Reminders[0].LastFiredLocalDate = null; whitelist.Reminders[0].BubbleGroup = "changed";
        Check(close.UpdateSettings(whitelist.Reminders, false, new(22,0), new(8,0)).Success &&
            close.GetSnapshot().Reminders[0].LastFiredLocalDate == new DateOnly(2026,10,31) && close.GetSnapshot().Reminders[0].BubbleGroup == "pair",
            "settings commands cannot change identity or acknowledgment-related metadata");

        ReminderData DstFixture() => new() { QuietHoursEnabled = false, Reminders =
            [new() { Id = "dst", Title = "Синтетический пункт", Time = new(3,30), MessageVariants = ["Проверка времени"] }] };
        var fall = new ReminderCommands(new ReminderStore(Seed("fall", DstFixture())));
        fall.Tick(DateTimeOffset.Parse("2026-10-25T00:30:00Z"), TimeSpan.Zero);
        fall = new ReminderCommands(new ReminderStore(fall.ConfigurationPath));
        fall.Tick(DateTimeOffset.Parse("2026-10-25T01:30:00Z"), TimeSpan.Zero);
        Check(fall.GetSnapshot().Occurrences.Count == 1, "Kyiv repeated autumn hour and restart cannot duplicate local-date identity");
        var spring = new ReminderCommands(new ReminderStore(Seed("spring", DstFixture())));
        spring.Tick(DateTimeOffset.Parse("2026-03-29T00:59:00Z"), TimeSpan.Zero);
        Check(spring.GetSnapshot().Occurrences.Count == 0, "Kyiv spring reminder not early");
        spring.Tick(DateTimeOffset.Parse("2026-03-29T01:00:00Z"), TimeSpan.Zero);
        Check(spring.GetSnapshot().Occurrences.Single().DueAtUtc == DateTimeOffset.Parse("2026-03-29T01:00:00Z"), "Kyiv skipped local time catches up after DST gap");

        string allowed = SpriteOutputPolicy.Resolve(workspace, null);
        Check(allowed == Path.Combine(workspace, "artifacts", "LegacySprites"), "legacy generator defaults to separate artifacts directory");
        foreach (var forbidden in new[] { "Assets/Sprites", Path.Combine(workspace, "Assets", "Sprites"), "artifacts/LegacySprites/../../Assets/Sprites" })
        {
            bool rejected = false;
            try { SpriteOutputPolicy.Resolve(workspace, forbidden); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "legacy output policy rejects production or traversal target");
        }
        // No SpriteBuilder entry point, renderer or generation code is executed by these checks.
        string policyFixture = Path.Combine(folder, "output-policy");
        string policyOutput = SpriteOutputPolicy.Resolve(policyFixture, null);
        Directory.CreateDirectory(policyOutput);
        using (var output = SpriteOutputPolicy.CreateFile(policyFixture, policyOutput, "sample.txt"))
        { output.Write(System.Text.Encoding.UTF8.GetBytes("preserve me")); }
        bool overwriteRejected = false;
        try { using var output = SpriteOutputPolicy.CreateFile(policyFixture, policyOutput, "sample.txt"); }
        catch (IOException) { overwriteRejected = true; }
        Check(overwriteRejected && File.ReadAllText(Path.Combine(policyOutput, "sample.txt")) == "preserve me",
            "legacy output files use create-new semantics and cannot overwrite existing file links or bytes");
        VerifySprites("After");
        var assembly = typeof(ReminderStore).Assembly; // Verify the desktop assembly actually built for this test configuration.
        using var reader = new ResourceReader(assembly.GetManifestResourceStream("ChihDesktop.g.resources")!);
        int embedded = 0;
        foreach (System.Collections.DictionaryEntry entry in reader)
        {
            string name = (string)entry.Key;
            if (!name.StartsWith("assets/sprites/", StringComparison.Ordinal) || !name.EndsWith(".png", StringComparison.Ordinal)) continue;
            var source = baseline.Single(x => x.File.Equals(Path.GetFileName(name), StringComparison.OrdinalIgnoreCase));
            using var bytes = new MemoryStream(); ((Stream)entry.Value!).CopyTo(bytes);
            Check(Convert.ToHexString(SHA256.HashData(bytes.ToArray())) == source.SHA256, "embedded resource unchanged " + source.File);
            embedded++;
        }
        Check(embedded == 15, "all protected PNGs embedded in application build");
        Console.WriteLine($"{count} persistence/artwork assertions passed. Fixtures: {folder}");
        Assert.Equal(94, count);
    }
sealed record SpriteHash(string File, string SHA256);
sealed class FailingCommitStore(string path) : ReminderStore(path)
{
    public bool FailPrimaryCommit { get; set; }
    protected override void CommitFile(string temporary, string destination)
    {
        if (FailPrimaryCommit && destination == FilePath) throw new IOException("Simulated primary commit failure.");
        base.CommitFile(temporary, destination);
    }
}
}

