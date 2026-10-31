using Microsoft.EntityFrameworkCore;

namespace ChihDesktop.Infrastructure;

public sealed class SqlitePlannerTaskStore(PlannerDatabaseExecutor executor) : IPlannerTaskStore
{
    private static PlannerResult<T> Missing<T>() => PlannerResult<T>.Fail(PlannerError.NotFound, "Задача не найдена.");
    private static PlannerResult<T> Conflict<T>() => PlannerResult<T>.Fail(PlannerError.Conflict, "Задача уже изменена. Обновите список.");

    public Task<PlannerResult<PlannerTask>> CreateAsync(PlannerTask task, CancellationToken cancellationToken) => executor.RunAsync(context =>
    {
        var row = new PlannerTaskRow(); row.Apply(task);
        context.Tasks.Add(row);
        cancellationToken.ThrowIfCancellationRequested();
        context.SaveChanges();
        return PlannerResult<PlannerTask>.Ok(row.ToDomain());
    }, cancellationToken);

    public Task<PlannerResult<PlannerTask>> ChangeAsync(Guid id, Func<PlannerTask, bool> change, long? expectedRevision, CancellationToken cancellationToken) => executor.RunAsync(context =>
    {
        var row = context.Tasks.SingleOrDefault(t => t.Id == id);
        if (row is null) return Missing<PlannerTask>();
        if (expectedRevision is { } revision && revision != row.Revision) return Conflict<PlannerTask>();
        var task = row.ToDomain();
        if (change(task))
        {
            row.Apply(task);
            cancellationToken.ThrowIfCancellationRequested();
            context.SaveChanges();
        }
        return PlannerResult<PlannerTask>.Ok(task);
    }, cancellationToken);

    public Task<PlannerResult<bool>> DeleteAsync(Guid id, long? expectedRevision, CancellationToken cancellationToken) => executor.RunAsync(context =>
    {
        var row = context.Tasks.SingleOrDefault(t => t.Id == id);
        if (row is null) return Missing<bool>();
        if (expectedRevision is { } revision && revision != row.Revision) return Conflict<bool>();
        context.Tasks.Remove(row);
        cancellationToken.ThrowIfCancellationRequested();
        context.SaveChanges();
        return PlannerResult<bool>.Ok(true);
    }, cancellationToken);

    public Task<PlannerResult<PlannerTask>> FindAsync(Guid id, CancellationToken cancellationToken) => executor.RunAsync(context =>
    {
        var row = context.Tasks.AsNoTracking().SingleOrDefault(t => t.Id == id);
        return row is null ? Missing<PlannerTask>() : PlannerResult<PlannerTask>.Ok(row.ToDomain());
    }, cancellationToken);

    public Task<PlannerResult<IReadOnlyList<PlannerTask>>> QueryAsync(PlannerQuery query, DateOnly today, CancellationToken cancellationToken) => executor.RunAsync(context =>
    {
        IQueryable<PlannerTaskRow> rows = context.Tasks.AsNoTracking();
        rows = query.View switch
        {
            PlannerView.Completed => rows.Where(t => t.Status == 1),
            PlannerView.Inbox => rows.Where(t => t.Status == 0 && t.DueDate == null),
            PlannerView.Today => rows.Where(t => t.Status == 0 && t.DueDate <= today),
            PlannerView.Upcoming => rows.Where(t => t.Status == 0 && t.DueDate > today),
            _ => rows.Where(t => t.Status == 0)
        };
        bool descending = query.Direction == PlannerSortDirection.Descending;
        IOrderedQueryable<PlannerTaskRow> sorted = query.Sort switch
        {
            PlannerSort.Priority => descending ? rows.OrderByDescending(t => t.Priority) : rows.OrderBy(t => t.Priority),
            PlannerSort.CreatedAt => descending ? rows.OrderByDescending(t => t.CreatedAtUtcTicks) : rows.OrderBy(t => t.CreatedAtUtcTicks),
            _ => descending ? rows.OrderBy(t => t.DueDate == null).ThenByDescending(t => t.DueDate)
                : rows.OrderBy(t => t.DueDate == null).ThenBy(t => t.DueDate)
        };
        sorted = sorted.ThenByDescending(t => t.Priority).ThenBy(t => t.DueDate == null).ThenBy(t => t.DueDate)
            .ThenBy(t => t.CreatedAtUtcTicks).ThenBy(t => t.Id);
        return PlannerResult<IReadOnlyList<PlannerTask>>.Ok(sorted.ToList().Select(t => t.ToDomain()).ToArray());
    }, cancellationToken);
}
