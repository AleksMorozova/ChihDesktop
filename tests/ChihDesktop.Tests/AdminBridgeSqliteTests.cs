using System.Text.Json;
using ChihDesktop.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace ChihDesktop.Tests;

public sealed class AdminBridgeSqliteTests
{
    [Fact]
    public void Bundled_frontend_exists_with_local_scripts_styles_and_restrictive_csp()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "AdminUi");
        string html = File.ReadAllText(Path.Combine(folder, "index.html"));
        Assert.Contains("connect-src 'none'", html);
        Assert.Contains("frame-src 'none'", html);
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(html, "(?:src|href)=\"(\\./assets/[^\"]+)\""))
            Assert.True(File.Exists(Path.Combine(folder, match.Groups[1].Value)), match.Groups[1].Value);
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(folder, "assets"), "*.js"));
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(folder, "assets"), "*.css"));
        Assert.Empty(Directory.GetFiles(folder, "*.map", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Serialized_bridge_workflow_uses_real_DI_and_temporary_Sqlite_without_reminder_IO()
    {
        string testRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ChihDesktop.AdminBridgeTests")) + Path.DirectorySeparatorChar;
        string directory = Path.Combine(testRoot, Guid.NewGuid().ToString("N"));
        try
        {
            using var host = DesktopComposition.CreateHost(clock: new Clock(), plannerDatabase: new(Path.Combine(directory, "planner.db")), userSettings: TestSettings.Create());
            await host.StartAsync(TestContext.Current.CancellationToken);
            using var bridge = new AdminBridge(host.Services.GetRequiredService<IPlannerService>());
            var input = new PlannerTaskInput("Synthetic bridge task", "Synthetic description", PlannerPriority.High, PlannerCategory.Study, new(2026, 10, 31));
            var created = Read<PlannerTaskDto>(await Send(bridge, "planner.create", new { task = input }));
            Assert.Equal(new DateOnly(2026, 10, 31), created.DueDate);
            Assert.Single(Read<PlannerTaskDto[]>(await Send(bridge, "planner.list", new { query = new PlannerQuery(PlannerView.Today) })));
            var updated = Read<PlannerTaskDto>(await Send(bridge, "planner.update", new { id = created.Id, expectedRevision = created.Revision, task = input with { Title = "Synthetic edited task" } }));
            Assert.Equal("Synthetic edited task", updated.Title);
            var stale = await Send(bridge, "planner.update", new { id = created.Id, expectedRevision = created.Revision, task = input });
            Assert.Equal("Conflict", stale.Error!.Code);
            var completed = Read<PlannerTaskDto>(await Send(bridge, "planner.complete", new { id = created.Id }));
            Assert.Equal(PlannerTaskStatus.Completed, completed.Status);
            Assert.NotNull(completed.CompletedAtUtc);
            Assert.Empty(Read<PlannerTaskDto[]>(await Send(bridge, "planner.list", new { query = new PlannerQuery(PlannerView.Today) })));
            Assert.Single(Read<PlannerTaskDto[]>(await Send(bridge, "planner.list", new { query = new PlannerQuery(PlannerView.Completed) })));
            var reopened = Read<PlannerTaskDto>(await Send(bridge, "planner.reopen", new { id = created.Id }));
            Assert.Null(reopened.CompletedAtUtc);
            Assert.True(Read<bool>(await Send(bridge, "planner.delete", new { id = created.Id, expectedRevision = reopened.Revision })));
            Assert.Equal("NotFound", (await Send(bridge, "planner.get", new { id = created.Id })).Error!.Code);
            await host.StopAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            if (!Path.GetFullPath(directory).StartsWith(testRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe test cleanup path.");
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static Task<AdminResponse> Send(AdminBridge bridge, string operation, object payload) => bridge.HandleAsync(JsonSerializer.Serialize(
        new { protocolVersion = 1, requestId = Guid.NewGuid().ToString(), operation, payload }, AdminBridge.JsonOptions), TestContext.Current.CancellationToken);
    private static T Read<T>(AdminResponse response)
    {
        Assert.True(response.Success, response.Error?.Message);
        using var json = JsonDocument.Parse(AdminBridge.Serialize(response));
        return json.RootElement.GetProperty("data").Deserialize<T>(AdminBridge.JsonOptions)!;
    }
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
    }
}
