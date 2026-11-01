using ChihDesktop.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChihDesktop.Tests;

public sealed class PlannerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Invalid_titles_do_not_initialize_database(string? title)
    {
        using var db = new Fixture();
        Assert.Equal(PlannerError.InvalidInput, (await db.Service.CreateTaskAsync(new(title!), cancellationToken: TestContext.Current.CancellationToken)).Error);
        Assert.Equal(PlannerDatabaseState.NotInitialized, db.Executor.State);
        Assert.False(File.Exists(db.Path));
    }

    [Fact]
    public async Task Validation_trimming_enums_and_required_identifiers()
    {
        using var db = new Fixture();
        Assert.Equal(PlannerError.InvalidInput, (await db.Service.CreateTaskAsync(new(new string('a', 201)), cancellationToken: TestContext.Current.CancellationToken)).Error);
        Assert.Equal(PlannerError.InvalidInput, (await db.Service.CreateTaskAsync(new("task", Priority: (PlannerPriority)7), cancellationToken: TestContext.Current.CancellationToken)).Error);
        Assert.Equal(PlannerError.InvalidInput, (await db.Service.CreateTaskAsync(new("task", Category: (PlannerCategory)7), cancellationToken: TestContext.Current.CancellationToken)).Error);
        Assert.Equal(PlannerError.InvalidInput, (await db.Service.GetTaskByIdAsync(Guid.Empty, cancellationToken: TestContext.Current.CancellationToken)).Error);
        Assert.Equal(PlannerError.InvalidInput, (await db.Service.GetTasksAsync(new((PlannerView)90), cancellationToken: TestContext.Current.CancellationToken)).Error);
        var task = await db.Create("  synthetic task  ");
        Assert.Equal("synthetic task", task.Title);
        Assert.Equal(PlannerTaskStatus.Active, task.Status);
        Assert.Equal(db.Clock.Now, task.CreatedAtUtc);
        Assert.Equal(task.CreatedAtUtc, task.UpdatedAtUtc);
        Assert.Null(task.CompletedAtUtc);
        Assert.Equal(1, task.Revision);
        Assert.True((await db.Service.CreateTaskAsync(new(new string('a', 200)), cancellationToken: TestContext.Current.CancellationToken)).Success);
    }

    [Fact]
    public async Task Update_complete_idempotence_reopen_delete_and_utc_clock()
    {
        using var db = new Fixture();
        var created = await db.Create("synthetic");
        db.Clock.Now = db.Clock.Now.AddHours(1);
        var input = new PlannerTaskInput("updated", "description", PlannerPriority.High, PlannerCategory.Work, new(2026, 11, 1));
        var updated = Value(await db.Service.UpdateTaskAsync(created.Id, input, created.Revision, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(input.Title, updated.Title);
        Assert.Equal(input.Description, updated.Description);
        Assert.Equal(input.Priority, updated.Priority);
        Assert.Equal(input.Category, updated.Category);
        Assert.Equal(input.DueDate, updated.DueDate);
        Assert.Equal(created.CreatedAtUtc, updated.CreatedAtUtc);
        Assert.Equal(db.Clock.Now, updated.UpdatedAtUtc);
        var completed = Value(await db.Service.CompleteTaskAsync(created.Id, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(PlannerTaskStatus.Completed, completed.Status);
        Assert.Equal(db.Clock.Now, completed.CompletedAtUtc);
        db.Clock.Now = db.Clock.Now.AddHours(1);
        Assert.Equal(completed, Value(await db.Service.CompleteTaskAsync(created.Id, cancellationToken: TestContext.Current.CancellationToken)));
        var editedCompleted = Value(await db.Service.UpdateTaskAsync(created.Id, input with { Title = "still completed" }, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(completed.CompletedAtUtc, editedCompleted.CompletedAtUtc);
        Assert.Equal(PlannerTaskStatus.Completed, editedCompleted.Status);
        var reopened = Value(await db.Service.ReopenTaskAsync(created.Id, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Null(reopened.CompletedAtUtc);
        Assert.Equal(PlannerTaskStatus.Active, reopened.Status);
        Assert.Equal(db.Clock.Now, reopened.UpdatedAtUtc);
        Assert.Equal(reopened, Value(await db.Service.ReopenTaskAsync(created.Id, cancellationToken: TestContext.Current.CancellationToken)));
        Assert.True((await db.Service.DeleteTaskAsync(created.Id, reopened.Revision, cancellationToken: TestContext.Current.CancellationToken)).Success);
        Assert.Equal(PlannerError.NotFound, (await db.Service.GetTaskByIdAsync(created.Id, cancellationToken: TestContext.Current.CancellationToken)).Error);
        Assert.Equal(PlannerError.NotFound, (await db.Service.DeleteTaskAsync(created.Id, cancellationToken: TestContext.Current.CancellationToken)).Error);
        var domain = PlannerTask.Create(new("offset"), new(2026, 10, 1, 10, 0, 0, TimeSpan.FromHours(3)));
        Assert.Equal(TimeSpan.Zero, domain.CreatedAtUtc.Offset);
        Assert.Equal(7, domain.CreatedAtUtc.Hour);
    }

    [Fact]
    public async Task Fresh_migration_and_persistence_across_factories_and_contexts()
    {
        using var db = new Fixture();
        Assert.True((await db.Executor.InitializeAsync(cancellationToken: TestContext.Current.CancellationToken)).Success);
        Assert.Equal(PlannerDatabaseState.Ready, db.Executor.State);
        using (var context = db.Factory.CreateDbContext())
        {
            Assert.Equal(2, context.Database.GetAppliedMigrations().Count());
            Assert.Contains("20261031154318_InitialPlanner", context.Database.GetAppliedMigrations());
            Assert.Empty(context.Database.GetPendingMigrations());
            Assert.False(context.Database.HasPendingModelChanges());
            Assert.Equal(2, context.Model.FindEntityType(typeof(PlannerTaskRow))!.GetIndexes().Count());
        }
        var created = await db.Create("persistent synthetic task");
        using var second = new PlannerDatabaseExecutor(new Factory(db.Options), db.Options, NullLogger<PlannerDatabaseExecutor>.Instance);
        var service = new PlannerService(new SqlitePlannerTaskStore(second), db.Clock);
        Assert.Equal(created, Value(await service.GetTaskByIdAsync(created.Id, cancellationToken: TestContext.Current.CancellationToken)));
        using var independent = db.Factory.CreateDbContext();
        Assert.Equal(created.Id, independent.Tasks.AsNoTracking().Single().Id);
    }

    [Fact]
    public async Task Views_include_overdue_and_exclude_completed()
    {
        using var db = new Fixture();
        var inbox = await db.Create("inbox");
        var overdue = await db.Create("overdue", new(2026, 10, 29));
        var today = await db.Create("today", new(2026, 10, 31));
        var future = await db.Create("future", new(2026, 11, 1));
        var done = await db.Create("done", new(2026, 10, 30));
        Assert.True((await db.Service.CompleteTaskAsync(done.Id, cancellationToken: TestContext.Current.CancellationToken)).Success);
        Assert.Equal(new[] { inbox.Id }, (await db.List(PlannerView.Inbox)).Select(t => t.Id));
        Assert.Equal(new[] { overdue.Id, today.Id }, (await db.List(PlannerView.Today)).Select(t => t.Id));
        Assert.Equal(new[] { future.Id }, (await db.List(PlannerView.Upcoming)).Select(t => t.Id));
        Assert.Equal(new[] { done.Id }, (await db.List(PlannerView.Completed)).Select(t => t.Id));
        Assert.Equal(4, (await db.List(PlannerView.AllActive)).Count);
    }

    [Theory]
    [InlineData("2026-03-28T21:30:00Z", "2026-03-29", false)]
    [InlineData("2026-03-28T23:30:00Z", "2026-03-29", true)]
    [InlineData("2026-03-29T21:30:00Z", "2026-03-30", true)]
    [InlineData("2026-10-24T21:30:00Z", "2026-10-25", true)]
    [InlineData("2026-10-25T21:30:00Z", "2026-10-26", false)]
    [InlineData("2026-10-25T22:30:00Z", "2026-10-26", true)]
    public async Task Today_uses_Kyiv_calendar_date_across_dst(string utc, string due, bool isToday)
    {
        using var db = new Fixture();
        db.Clock.Now = DateTimeOffset.Parse(utc);
        await db.Create("date boundary", DateOnly.Parse(due));
        Assert.Equal(isToday ? 1 : 0, (await db.List(PlannerView.Today)).Count);
        Assert.Equal(isToday ? 0 : 1, (await db.List(PlannerView.Upcoming)).Count);
    }

    [Theory]
    [InlineData(PlannerSort.DueDate, PlannerSortDirection.Ascending, "early,late,undated")]
    [InlineData(PlannerSort.DueDate, PlannerSortDirection.Descending, "late,early,undated")]
    [InlineData(PlannerSort.Priority, PlannerSortDirection.Ascending, "late,undated,early")]
    [InlineData(PlannerSort.Priority, PlannerSortDirection.Descending, "early,undated,late")]
    [InlineData(PlannerSort.CreatedAt, PlannerSortDirection.Ascending, "undated,late,early")]
    [InlineData(PlannerSort.CreatedAt, PlannerSortDirection.Descending, "early,late,undated")]
    public async Task Sorts_are_explicit_and_undated_is_last_for_due_sort(PlannerSort sort, PlannerSortDirection direction, string expected)
    {
        using var db = new Fixture();
        await db.Create("undated"); db.Clock.Now += TimeSpan.FromMinutes(1);
        await db.Create("late", new(2026, 11, 2), PlannerPriority.Low); db.Clock.Now += TimeSpan.FromMinutes(1);
        await db.Create("early", new(2026, 11, 1), PlannerPriority.High);
        var tasks = Value(await db.Service.GetTasksAsync(new(Sort: sort, Direction: direction), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(expected, string.Join(',', tasks.Select(t => t.Title)));
    }

    [Fact]
    public async Task Concurrent_edits_with_same_revision_have_one_winner()
    {
        using var db = new Fixture();
        var task = await db.Create("original");
        var results = await Task.WhenAll(
            db.Service.UpdateTaskAsync(task.Id, new("first"), task.Revision, cancellationToken: TestContext.Current.CancellationToken),
            db.Service.UpdateTaskAsync(task.Id, new("second"), task.Revision, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Single(results, t => t.Success);
        Assert.Single(results, t => t.Error == PlannerError.Conflict);
        Assert.Equal(task.Revision + 1, Value(await db.Service.GetTaskByIdAsync(task.Id, cancellationToken: TestContext.Current.CancellationToken)).Revision);
        Assert.Equal(PlannerError.Conflict, (await db.Service.DeleteTaskAsync(task.Id, task.Revision, cancellationToken: TestContext.Current.CancellationToken)).Error);
        // Without a supplied revision, commands intentionally use the latest row under the gate.
        var completions = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => db.Service.CompleteTaskAsync(task.Id, cancellationToken: TestContext.Current.CancellationToken)));
        Assert.All(completions, result => Assert.Equal(task.Revision + 2, Value(result).Revision));
    }

    [Fact]
    public async Task Ef_revision_token_detects_a_writer_outside_the_gate()
    {
        using var db = new Fixture();
        await db.Create("original");
        using var a = db.Factory.CreateDbContext();
        using var b = db.Factory.CreateDbContext();
        var first = a.Tasks.Single(); var stale = b.Tasks.Single();
        first.Title = "winner"; first.Revision++;
        a.SaveChanges();
        stale.Title = "stale"; stale.Revision++;
        Assert.Throws<DbUpdateConcurrencyException>(() => b.SaveChanges());
        Assert.Equal("winner", (await db.List(PlannerView.AllActive)).Single().Title);
    }

    [Fact]
    public async Task Failed_write_never_reports_completion_or_changes_durable_state()
    {
        var failure = new WriteFailure();
        using var db = new Fixture(failure);
        var task = await db.Create("original");
        failure.Fail = true;
        Assert.Equal(PlannerError.PersistenceUnavailable, (await db.Service.CompleteTaskAsync(task.Id, cancellationToken: TestContext.Current.CancellationToken)).Error);
        Assert.Equal(PlannerError.PersistenceUnavailable, (await db.Service.UpdateTaskAsync(task.Id, new("lost"), cancellationToken: TestContext.Current.CancellationToken)).Error);
        Assert.Equal(PlannerError.PersistenceUnavailable, (await db.Service.DeleteTaskAsync(task.Id, cancellationToken: TestContext.Current.CancellationToken)).Error);
        Assert.Equal(PlannerError.PersistenceUnavailable, (await db.Service.CreateTaskAsync(new("lost"), cancellationToken: TestContext.Current.CancellationToken)).Error);
        Assert.Equal(task, Value(await db.Service.GetTaskByIdAsync(task.Id, cancellationToken: TestContext.Current.CancellationToken)));
        failure.Fail = false;
        Assert.True((await db.Service.CompleteTaskAsync(task.Id, cancellationToken: TestContext.Current.CancellationToken)).Success);
    }

    [Fact]
    public async Task Unknown_future_schema_is_not_downgraded()
    {
        using var db = new Fixture();
        Assert.True((await db.Executor.InitializeAsync(cancellationToken: TestContext.Current.CancellationToken)).Success);
        using (var context = db.Factory.CreateDbContext())
            context.Database.ExecuteSqlRaw("INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('99999999999999_Future', '99.0.0')");
        using var next = new PlannerDatabaseExecutor(db.Factory, db.Options, NullLogger<PlannerDatabaseExecutor>.Instance);
        Assert.Equal(PlannerError.MigrationFailed, (await next.InitializeAsync(cancellationToken: TestContext.Current.CancellationToken)).Error);
        Assert.Equal(PlannerDatabaseState.Unavailable, next.State);
        using var verify = db.Factory.CreateDbContext();
        Assert.Contains("99999999999999_Future", verify.Database.GetAppliedMigrations());
    }

    [Fact]
    public async Task Failed_migration_preserves_existing_database_and_blocks_commands()
    {
        using var db = new Fixture();
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(db.Path)!);
        using (var connection = new SqliteConnection(db.Options.ConnectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE PlannerTasks (Sentinel TEXT); INSERT INTO PlannerTasks VALUES ('preserve me');";
            command.ExecuteNonQuery();
        }
        Assert.Equal(PlannerError.MigrationFailed, (await db.Executor.InitializeAsync(cancellationToken: TestContext.Current.CancellationToken)).Error);
        Assert.Equal(PlannerError.MigrationFailed, (await db.Service.CreateTaskAsync(new("blocked"), cancellationToken: TestContext.Current.CancellationToken)).Error);
        using var verify = new SqliteConnection(db.Options.ConnectionString); verify.Open();
        using var query = verify.CreateCommand(); query.CommandText = "SELECT Sentinel FROM PlannerTasks";
        Assert.Equal("preserve me", query.ExecuteScalar());
    }

    [Fact]
    public async Task Corrupt_database_bytes_are_not_reset()
    {
        using var db = new Fixture();
        byte[] original = "synthetic invalid SQLite file"u8.ToArray();
        File.WriteAllBytes(db.Path, original);
        Assert.Equal(PlannerError.MigrationFailed, (await db.Executor.InitializeAsync(cancellationToken: TestContext.Current.CancellationToken)).Error);
        Assert.Equal(original, File.ReadAllBytes(db.Path));
    }

    [Fact]
    public async Task Interrupted_migration_lock_is_cancellable_and_is_not_deleted()
    {
        using var db = new Fixture();
        using (var connection = new SqliteConnection(db.Options.ConnectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE __EFMigrationsLock (Id INTEGER NOT NULL PRIMARY KEY, Timestamp TEXT NOT NULL); INSERT INTO __EFMigrationsLock VALUES (1, '2026-01-01T00:00:00Z');";
            command.ExecuteNonQuery();
        }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(250));
        var outcome = await db.Executor.InitializeAsync(cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(PlannerError.Cancelled, outcome.Error);
        Assert.Equal(PlannerDatabaseState.NotInitialized, db.Executor.State);
        using var verify = new SqliteConnection(db.Options.ConnectionString); verify.Open();
        using var query = verify.CreateCommand(); query.CommandText = "SELECT COUNT(*) FROM __EFMigrationsLock WHERE Id = 1";
        Assert.Equal(1L, query.ExecuteScalar());
    }

    [Fact]
    public async Task Cancelled_command_does_not_write()
    {
        using var db = new Fixture();
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Equal(PlannerError.Cancelled, (await db.Service.CreateTaskAsync(new("cancelled"), cancelled.Token)).Error);
        Assert.False(File.Exists(db.Path));
        await db.Create("kept");
        Assert.Equal(PlannerError.Cancelled, (await db.Executor.InitializeAsync(cancelled.Token)).Error);
        Assert.Single(await db.List(PlannerView.AllActive));
    }

    [Fact]
    public async Task Sqlite_work_does_not_block_the_calling_thread_and_queued_cancellation_is_safe()
    {
        using var db = new Fixture();
        Assert.True((await db.Executor.InitializeAsync(cancellationToken: TestContext.Current.CancellationToken)).Success);
        using var release = new ManualResetEventSlim();
        using var started = new ManualResetEventSlim();
        int caller = 0, worker = 0;
        Task<PlannerResult<bool>>? first = null;
        var thread = new Thread(() =>
        {
            caller = Environment.CurrentManagedThreadId;
            first = db.Executor.RunAsync(_ =>
            {
                worker = Environment.CurrentManagedThreadId; started.Set();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new InvalidOperationException("Test boundary timeout.");
                return PlannerResult<bool>.Ok(true);
            }, CancellationToken.None);
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        try
        {
            Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "Initiating a database operation blocked the UI thread.");
            Assert.True(started.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.NotEqual(caller, worker);
            Assert.False(first!.IsCompleted);
            using var cancellation = new CancellationTokenSource();
            var waiting = db.Service.CreateTaskAsync(new("must not write"), cancellation.Token);
            cancellation.Cancel();
            Assert.Equal(PlannerError.Cancelled, (await waiting.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).Error);
        }
        finally { release.Set(); }
        Assert.True((await first!).Success);
        Assert.Empty(await db.List(PlannerView.AllActive));
    }

    [Fact]
    public async Task Desktop_DI_uses_explicit_temporary_database_and_starts_without_eager_IO()
    {
        using var db = new Fixture();
        using var host = DesktopComposition.CreateHost(plannerDatabase: db.Options, userSettings: TestSettings.Create());
        await host.StartAsync(TestContext.Current.CancellationToken);
        Assert.False(File.Exists(db.Path));
        Assert.Empty(host.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>());
        var initializer = host.Services.GetRequiredService<IPlannerDatabaseInitializer>();
        Assert.True((await initializer.InitializeAsync(cancellationToken: TestContext.Current.CancellationToken)).Success);
        Assert.True(File.Exists(db.Path));
        var service = host.Services.GetRequiredService<IPlannerService>();
        Assert.True((await service.CreateTaskAsync(new("DI synthetic task"), cancellationToken: TestContext.Current.CancellationToken)).Success);
        Assert.Single(Value(await service.GetTasksAsync(cancellationToken: TestContext.Current.CancellationToken)));
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Core_and_infrastructure_boundaries_are_portable()
    {
        Assert.DoesNotContain(typeof(PlannerTask).Assembly.GetReferencedAssemblies(), r =>
            r.Name!.Contains("EntityFramework") || r.Name.Contains("Infrastructure") || r.Name.Contains("Sqlite"));
        Assert.DoesNotContain(typeof(PlannerDbContext).Assembly.GetReferencedAssemblies(), r =>
            r.Name is "ChihDesktop" or "PresentationCore" or "PresentationFramework" or "WindowsBase");
        Assert.NotEqual(typeof(PlannerTask).Assembly, typeof(PlannerDbContext).Assembly);
        Assert.False(typeof(PlannerTaskRow).IsPublic);
    }

    private static T Value<T>(PlannerResult<T> result)
    {
        Assert.True(result.Success, result.Message);
        return result.Value!;
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class WriteFailure : SaveChangesInterceptor
    {
        public bool Fail;
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            if (Fail) throw new DbUpdateException("Synthetic write failure");
            return result;
        }
    }

    private sealed class Factory(PlannerDatabaseOptions options, WriteFailure? failure = null) : IDbContextFactory<PlannerDbContext>
    {
        public PlannerDbContext CreateDbContext()
        {
            var builder = new DbContextOptionsBuilder<PlannerDbContext>().UseSqlite(options.ConnectionString);
            if (failure is not null) builder.AddInterceptors(failure);
            return new(builder.Options);
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ChihDesktop.PlannerTests", Guid.NewGuid().ToString("N"));
        public string Path => Options.DatabasePath;
        public PlannerDatabaseOptions Options { get; }
        public Factory Factory { get; }
        public Clock Clock { get; } = new();
        public PlannerDatabaseExecutor Executor { get; }
        public IPlannerService Service { get; }
        public Fixture(WriteFailure? failure = null)
        {
            Directory.CreateDirectory(directory);
            Options = new(System.IO.Path.Combine(directory, "test.db"));
            Factory = new(Options, failure);
            Executor = new(Factory, Options, NullLogger<PlannerDatabaseExecutor>.Instance);
            Service = new PlannerService(new SqlitePlannerTaskStore(Executor), Clock);
        }
        public async Task<PlannerTaskDto> Create(string title, DateOnly? due = null, PlannerPriority priority = PlannerPriority.Normal) =>
            Value(await Service.CreateTaskAsync(new(title, Priority: priority, DueDate: due), cancellationToken: TestContext.Current.CancellationToken));
        public async Task<IReadOnlyList<PlannerTaskDto>> List(PlannerView view) => Value(await Service.GetTasksAsync(new(view), cancellationToken: TestContext.Current.CancellationToken));
        public void Dispose()
        {
            Executor.Dispose();
            string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ChihDesktop.PlannerTests")) + System.IO.Path.DirectorySeparatorChar;
            if (!System.IO.Path.GetFullPath(directory).StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe test cleanup path.");
            Directory.Delete(directory, true);
        }
    }
}
