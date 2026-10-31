using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ChihDesktop.Infrastructure;

// SQLite's synchronous disk work stays on the pool, behind a single asynchronous gate.
// No context or connection crosses the boundary; no dedicated worker runs while idle.
public sealed class PlannerDatabaseExecutor(IDbContextFactory<PlannerDbContext> factory,
    PlannerDatabaseOptions options, ILogger<PlannerDatabaseExecutor> logger) : IPlannerDatabaseInitializer, IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private volatile PlannerDatabaseState state;
    public PlannerDatabaseState State => state;

    public Task<PlannerResult<bool>> InitializeAsync(CancellationToken cancellationToken = default) =>
        RunAsync(_ => PlannerResult<bool>.Ok(true), cancellationToken);

    internal async Task<PlannerResult<T>> RunAsync<T>(Func<PlannerDbContext, PlannerResult<T>> operation, CancellationToken token)
    {
        bool entered = false;
        try
        {
            await gate.WaitAsync(token).ConfigureAwait(false);
            entered = true;
            return await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                if (state == PlannerDatabaseState.Unavailable) return Failure<T>(PlannerError.MigrationFailed);
                if (state != PlannerDatabaseState.Ready)
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(options.DatabasePath)!);
                        using var migrationContext = factory.CreateDbContext();
                        var known = migrationContext.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
                        if (known.Count == 0 || migrationContext.Database.GetAppliedMigrations().Any(id => !known.Contains(id)))
                            throw new InvalidOperationException("Unknown planner schema; use a compatible application version.");
                        // EF's migration-lock retry can otherwise wait indefinitely after a crash.
                        // Async migration honors cancellation; even its synchronous SQLite work is
                        // still inside this pool-thread boundary, never on the Dispatcher.
                        using var migrationTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                        migrationTimeout.CancelAfter(TimeSpan.FromSeconds(30));
                        migrationContext.Database.MigrateAsync(migrationTimeout.Token).GetAwaiter().GetResult();
                        state = PlannerDatabaseState.Ready;
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        state = PlannerDatabaseState.NotInitialized;
                        throw;
                    }
                    catch (Exception ex)
                    {
                        state = PlannerDatabaseState.Unavailable;
                        logger.LogError("Planner migration failed ({ExceptionType}); database was preserved.", ex.GetType().Name);
                        return Failure<T>(PlannerError.MigrationFailed);
                    }
                }
                token.ThrowIfCancellationRequested();
                using var context = factory.CreateDbContext();
                return operation(context);
            }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return Failure<T>(PlannerError.Cancelled); }
        catch (DbUpdateConcurrencyException) { return Failure<T>(PlannerError.Conflict); }
        catch (Exception ex) when (ex is DbUpdateException or SqliteException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            logger.LogWarning("Planner operation failed ({ExceptionType}).", ex.GetType().Name);
            return Failure<T>(ex is SqliteException { SqliteErrorCode: 5 or 6 } ? PlannerError.Conflict : PlannerError.PersistenceUnavailable);
        }
        finally { if (entered) gate.Release(); }
    }

    private static PlannerResult<T> Failure<T>(PlannerError error) => PlannerResult<T>.Fail(error, error switch
    {
        PlannerError.Cancelled => "Действие отменено.",
        PlannerError.Conflict => "Задача уже изменена или база занята. Обновите список и повторите действие.",
        PlannerError.MigrationFailed => "Планировщик недоступен: не удалось проверить или обновить базу. Файл сохранён. Проверьте права, свободное место и версию приложения; затем перезапустите Чиха.",
        _ => "Не удалось сохранить или прочитать задачу. Проверьте доступ к базе и свободное место. Изменение не подтверждено."
    });
    public void Dispose() => gate.Dispose();
}
