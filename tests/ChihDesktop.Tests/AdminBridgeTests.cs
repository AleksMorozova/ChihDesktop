using System.Text.Json;
using Xunit;

namespace ChihDesktop.Tests;

public sealed class AdminBridgeTests
{
    private static readonly Guid TaskId = Guid.Parse("fe69b276-b987-46dd-8dbf-75e7d0d6fcd2");
    private static string Request(string operation, object? payload, string? id = null, int version = 1) => JsonSerializer.Serialize(
        new { protocolVersion = version, requestId = id ?? Guid.NewGuid().ToString(), operation, payload }, AdminBridge.JsonOptions);
    private static PlannerTaskInput Input => new("Synthetic title <script>not executed</script>", "Synthetic description", PlannerPriority.High, PlannerCategory.Study, new(2026, 10, 31));

    [Theory]
    [InlineData("planner.list")]
    [InlineData("planner.get")]
    [InlineData("planner.create")]
    [InlineData("planner.update")]
    [InlineData("planner.complete")]
    [InlineData("planner.reopen")]
    [InlineData("planner.delete")]
    public async Task Allowlisted_operations_reach_existing_application_service(string operation)
    {
        var planner = new FakePlanner(); using var bridge = new AdminBridge(planner);
        object payload = operation switch
        {
            "planner.list" => new { query = new PlannerQuery(PlannerView.Upcoming, PlannerSort.Priority, PlannerSortDirection.Descending) },
            "planner.create" => new { task = Input },
            "planner.update" => new { id = TaskId, task = Input, expectedRevision = 4 },
            "planner.delete" => new { id = TaskId, expectedRevision = 4 },
            _ => new { id = TaskId }
        };
        string id = Guid.NewGuid().ToString();
        var result = await bridge.HandleAsync(Request(operation, payload, id), TestContext.Current.CancellationToken);
        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal(id, result.RequestId);
        Assert.Equal(operation, planner.Operation);
        Assert.Equal(operation, result.Operation);
        if (operation == "planner.list") Assert.Equal(new PlannerQuery(PlannerView.Upcoming, PlannerSort.Priority, PlannerSortDirection.Descending), planner.Query);
        if (operation is "planner.create" or "planner.update") Assert.Equal(Input, planner.Input);
        if (operation is "planner.update" or "planner.delete") Assert.Equal(4, planner.Revision);
    }

    [Fact]
    public async Task Serialization_uses_camel_case_string_enums_and_DateOnly_without_timezone_conversion()
    {
        using var bridge = new AdminBridge(new FakePlanner());
        var result = await bridge.HandleAsync(Request("planner.get", new { id = TaskId }), TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(AdminBridge.Serialize(result));
        var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("protocolVersion").GetInt32());
        Assert.Equal("2026-10-31", root.GetProperty("data").GetProperty("dueDate").GetString());
        Assert.Equal("High", root.GetProperty("data").GetProperty("priority").GetString());
        Assert.Equal("Active", root.GetProperty("data").GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        Assert.DoesNotContain("<script>", AdminBridge.Serialize(result));
    }

    [Theory]
    [InlineData("filesystem.read")]
    [InlineData("sql.execute")]
    [InlineData("GetType")]
    [InlineData("")]
    public async Task Invalid_operations_never_invoke_native_services(string operation)
    {
        var planner = new FakePlanner(); using var bridge = new AdminBridge(planner);
        var result = await bridge.HandleAsync(Request(operation, new { }), TestContext.Current.CancellationToken);
        Assert.Equal("InvalidOperation", result.Error!.Code); Assert.Null(planner.Operation);
    }

    [Theory]
    [InlineData("planner.get", "{}")]
    [InlineData("planner.get", "{\"id\":\"not-a-guid\"}")]
    [InlineData("planner.get", "{\"id\":\"fe69b276-b987-46dd-8dbf-75e7d0d6fcd2\",\"path\":\"private\"}")]
    [InlineData("planner.list", "{\"query\":null}")]
    [InlineData("planner.create", "{\"task\":null}")]
    [InlineData("planner.create", "{\"task\":{\"title\":\"x\",\"priority\":2}}")]
    [InlineData("planner.create", "{\"task\":{\"title\":\"x\",\"dueDate\":\"2026-02-30\"}}")]
    [InlineData("planner.update", "{\"id\":\"fe69b276-b987-46dd-8dbf-75e7d0d6fcd2\",\"task\":{\"title\":\"x\"}}")]
    [InlineData("planner.delete", "null")]
    [InlineData("planner.delete", "{\"id\":\"fe69b276-b987-46dd-8dbf-75e7d0d6fcd2\",\"expectedRevision\":\"4\"}")]
    public async Task Invalid_payloads_are_rejected(string operation, string payloadJson)
    {
        var planner = new FakePlanner(); using var bridge = new AdminBridge(planner);
        var payload = JsonSerializer.Deserialize<JsonElement>(payloadJson);
        var result = await bridge.HandleAsync(Request(operation, payload), TestContext.Current.CancellationToken);
        Assert.False(result.Success); Assert.Equal("InvalidPayload", result.Error!.Code); Assert.Null(planner.Operation);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"protocolVersion\":1,\"protocolVersion\":2}")]
    public async Task Malformed_envelopes_return_safe_uncorrelated_error(string value)
    {
        using var bridge = new AdminBridge(new FakePlanner());
        var result = await bridge.HandleAsync(value, TestContext.Current.CancellationToken);
        Assert.False(result.Success); Assert.Null(result.RequestId); Assert.Equal("InvalidMessage", result.Error!.Code);
    }

    [Fact]
    public async Task Version_mismatch_and_oversized_messages_are_rejected()
    {
        var planner = new FakePlanner(); using var bridge = new AdminBridge(planner);
        var mismatch = await bridge.HandleAsync(Request("planner.list", new { query = new PlannerQuery() }, version: 99), TestContext.Current.CancellationToken);
        Assert.Equal("VersionMismatch", mismatch.Error!.Code);
        var oversized = await bridge.HandleAsync(new string('x', AdminBridge.MaxMessageLength + 1), TestContext.Current.CancellationToken);
        Assert.Equal("InvalidMessage", oversized.Error!.Code); Assert.Null(planner.Operation);
    }

    [Fact]
    public async Task Duplicate_ids_are_rejected_in_flight_and_after_completion()
    {
        var planner = new FakePlanner { Block = new(TaskCreationOptions.RunContinuationsAsynchronously) }; using var bridge = new AdminBridge(planner);
        string id = Guid.NewGuid().ToString();
        var pending = bridge.HandleAsync(Request("planner.list", new { query = new PlannerQuery() }, id), TestContext.Current.CancellationToken);
        Assert.Equal("DuplicateRequest", (await bridge.HandleAsync(Request("planner.list", new { query = new PlannerQuery() }, id.ToUpperInvariant()), TestContext.Current.CancellationToken)).Error!.Code);
        planner.Block.SetResult(); Assert.True((await pending).Success);
        Assert.Equal("DuplicateRequest", (await bridge.HandleAsync(Request("planner.delete", new { id = TaskId, expectedRevision = 1 }, id), TestContext.Current.CancellationToken)).Error!.Code);
        Assert.Equal(1, planner.Calls);
    }

    [Fact]
    public async Task In_flight_requests_are_bounded_and_disposal_cancels_pending_work()
    {
        var planner = new FakePlanner { Block = new(TaskCreationOptions.RunContinuationsAsynchronously) }; using var bridge = new AdminBridge(planner);
        var pending = Enumerable.Range(0, AdminBridge.MaxInFlight).Select(_ => bridge.HandleAsync(Request("planner.list", new { query = new PlannerQuery() }), TestContext.Current.CancellationToken)).ToArray();
        Assert.Equal("Busy", (await bridge.HandleAsync(Request("planner.list", new { query = new PlannerQuery() }), TestContext.Current.CancellationToken)).Error!.Code);
        bridge.Dispose();
        Assert.All(await Task.WhenAll(pending), response => Assert.Equal("Cancelled", response.Error!.Code));
        Assert.Equal("Disposed", (await bridge.HandleAsync(Request("planner.list", new { query = new PlannerQuery() }), TestContext.Current.CancellationToken)).Error!.Code);
    }

    [Fact]
    public async Task Session_id_history_is_bounded_without_eviction_or_replay()
    {
        using var bridge = new AdminBridge(new FakePlanner());
        for (int i = 0; i < AdminBridge.MaxRequestsPerSession; i++)
            Assert.True((await bridge.HandleAsync(Request("planner.list", new { query = new PlannerQuery() }), TestContext.Current.CancellationToken)).Success);
        Assert.Equal("SessionLimit", (await bridge.HandleAsync(Request("planner.list", new { query = new PlannerQuery() }), TestContext.Current.CancellationToken)).Error!.Code);
    }

    [Fact]
    public async Task Persistence_errors_and_unexpected_exceptions_do_not_report_success_or_leak_details()
    {
        var planner = new FakePlanner { Fail = true }; using var bridge = new AdminBridge(planner);
        var response = await bridge.HandleAsync(Request("planner.complete", new { id = TaskId }), TestContext.Current.CancellationToken);
        Assert.False(response.Success); Assert.Null(response.Data); Assert.Equal("PersistenceUnavailable", response.Error!.Code);
        planner.Throw = true;
        response = await bridge.HandleAsync(Request("planner.list", new { query = new PlannerQuery() }), TestContext.Current.CancellationToken);
        Assert.Equal("InternalError", response.Error!.Code);
        Assert.DoesNotContain("private-data", AdminBridge.Serialize(response));
    }

    [Theory]
    [InlineData("https://chih-admin.invalid/index.html", true)]
    [InlineData("https://chih-admin.invalid/assets/app.js", true)]
    [InlineData("http://chih-admin.invalid/index.html", false)]
    [InlineData("https://chih-admin.invalid.evil.test/", false)]
    [InlineData("https://evil.test/", false)]
    [InlineData("https://user@chih-admin.invalid/", false)]
    [InlineData("https://chih-admin.invalid:444/", false)]
    [InlineData("file:///C:/private.txt", false)]
    [InlineData("javascript:alert(1)", false)]
    public void Local_origin_is_explicit_and_external_links_are_exactly_allowlisted(string uri, bool expected)
    {
        Assert.Equal(expected, AdminNavigationPolicy.IsLocal(uri));
        Assert.False(AdminNavigationPolicy.IsApprovedExternal(uri));
        Assert.True(AdminNavigationPolicy.IsApprovedExternal(AdminNavigationPolicy.RuntimeHelp));
    }

    private sealed class FakePlanner : IPlannerService
    {
        public string? Operation; public PlannerTaskInput? Input; public PlannerQuery? Query; public long? Revision;
        public int Calls; public bool Fail, Throw; public TaskCompletionSource? Block;
        private static PlannerTaskDto Dto => PlannerTask.Create(AdminBridgeTests.Input, new(2026, 10, 31, 10, 0, 0, TimeSpan.Zero)).ToDto();
        private PlannerResult<T> Reply<T>(string operation, T value)
        {
            Operation = operation; Calls++;
            if (Throw) throw new InvalidOperationException("private-data");
            return Fail ? PlannerResult<T>.Fail(PlannerError.PersistenceUnavailable, "Проверьте доступ к диску.") : PlannerResult<T>.Ok(value);
        }
        public Task<PlannerResult<PlannerTaskDto>> CreateTaskAsync(PlannerTaskInput input, CancellationToken cancellationToken = default) { Input = input; return Task.FromResult(Reply("planner.create", Dto)); }
        public Task<PlannerResult<PlannerTaskDto>> UpdateTaskAsync(Guid id, PlannerTaskInput input, long? expectedRevision = null, CancellationToken cancellationToken = default) { Input = input; Revision = expectedRevision; return Task.FromResult(Reply("planner.update", Dto)); }
        public Task<PlannerResult<PlannerTaskDto>> CompleteTaskAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Reply("planner.complete", Dto));
        public Task<PlannerResult<PlannerTaskDto>> ReopenTaskAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Reply("planner.reopen", Dto));
        public Task<PlannerResult<bool>> DeleteTaskAsync(Guid id, long? expectedRevision = null, CancellationToken cancellationToken = default) { Revision = expectedRevision; return Task.FromResult(Reply("planner.delete", true)); }
        public Task<PlannerResult<PlannerTaskDto>> GetTaskByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Reply("planner.get", Dto));
        public async Task<PlannerResult<IReadOnlyList<PlannerTaskDto>>> GetTasksAsync(PlannerQuery? query = null, CancellationToken cancellationToken = default)
        {
            Query = query;
            var response = Reply<IReadOnlyList<PlannerTaskDto>>("planner.list", [Dto]);
            if (Block is not null) await Block.Task.WaitAsync(cancellationToken);
            return response;
        }
    }
}
