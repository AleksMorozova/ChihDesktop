namespace ChihDesktop;

public enum PlannerError { None, InvalidInput, NotFound, Conflict, PersistenceUnavailable, MigrationFailed, Cancelled }
public sealed record PlannerResult<T>(bool Success, T? Value, PlannerError Error, string? Message)
{
    public static PlannerResult<T> Ok(T value) => new(true, value, PlannerError.None, null);
    public static PlannerResult<T> Fail(PlannerError error, string message) => new(false, default, error, message);
}

public interface IPlannerTaskStore
{
    Task<PlannerResult<PlannerTask>> CreateAsync(PlannerTask task, CancellationToken cancellationToken);
    // Runs read/change/write atomically inside the adapter's serialized execution boundary.
    Task<PlannerResult<PlannerTask>> ChangeAsync(Guid id, Func<PlannerTask, bool> change, long? expectedRevision, CancellationToken cancellationToken);
    Task<PlannerResult<bool>> DeleteAsync(Guid id, long? expectedRevision, CancellationToken cancellationToken);
    Task<PlannerResult<PlannerTask>> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<PlannerResult<IReadOnlyList<PlannerTask>>> QueryAsync(PlannerQuery query, DateOnly today, CancellationToken cancellationToken);
}

public enum PlannerDatabaseState { NotInitialized, Ready, Unavailable }
public interface IPlannerDatabaseInitializer
{
    PlannerDatabaseState State { get; }
    Task<PlannerResult<bool>> InitializeAsync(CancellationToken cancellationToken = default);
}

public interface IPlannerService
{
    Task<PlannerResult<PlannerTaskDto>> CreateTaskAsync(PlannerTaskInput input, CancellationToken cancellationToken = default);
    Task<PlannerResult<PlannerTaskDto>> UpdateTaskAsync(Guid id, PlannerTaskInput input, long? expectedRevision = null, CancellationToken cancellationToken = default);
    Task<PlannerResult<PlannerTaskDto>> CompleteTaskAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PlannerResult<PlannerTaskDto>> ReopenTaskAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PlannerResult<bool>> DeleteTaskAsync(Guid id, long? expectedRevision = null, CancellationToken cancellationToken = default);
    Task<PlannerResult<PlannerTaskDto>> GetTaskByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PlannerResult<IReadOnlyList<PlannerTaskDto>>> GetTasksAsync(PlannerQuery? query = null, CancellationToken cancellationToken = default);
}

public sealed class PlannerService(IPlannerTaskStore store, TimeProvider clock, Func<string>? preferredTimeZone = null) : IPlannerService
{
    private static readonly TimeZoneInfo Kyiv = FindKyiv();
    private static TimeZoneInfo FindKyiv()
    {
        foreach (string id in new[] { "Europe/Kyiv", "Europe/Kiev", "FLE Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        throw new TimeZoneNotFoundException("Kyiv time-zone rules are unavailable.");
    }

    private static PlannerResult<T> Invalid<T>(string message) => PlannerResult<T>.Fail(PlannerError.InvalidInput, message);
    private static PlannerResult<T> Cancelled<T>() => PlannerResult<T>.Fail(PlannerError.Cancelled, "Действие отменено.");
    private static PlannerResult<PlannerTaskDto> Map(PlannerResult<PlannerTask> result) => result.Success
        ? PlannerResult<PlannerTaskDto>.Ok(result.Value!.ToDto())
        : PlannerResult<PlannerTaskDto>.Fail(result.Error, result.Message!);

    public async Task<PlannerResult<PlannerTaskDto>> CreateTaskAsync(PlannerTaskInput input, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return Cancelled<PlannerTaskDto>();
        if (input is null) return Invalid<PlannerTaskDto>("Укажите задачу.");
        PlannerTask task;
        try { task = PlannerTask.Create(input, clock.GetUtcNow()); }
        catch (ArgumentException ex) { return Invalid<PlannerTaskDto>(ex.Message); }
        return Map(await store.CreateAsync(task, cancellationToken).ConfigureAwait(false));
    }

    public async Task<PlannerResult<PlannerTaskDto>> UpdateTaskAsync(Guid id, PlannerTaskInput input, long? expectedRevision = null, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return Cancelled<PlannerTaskDto>();
        if (id == Guid.Empty || input is null || expectedRevision is < 1) return Invalid<PlannerTaskDto>("Проверьте ID и версию задачи.");
        try { input = input.Validate(); }
        catch (ArgumentException ex) { return Invalid<PlannerTaskDto>(ex.Message); }
        return Map(await store.ChangeAsync(id, task => task.Update(input, clock.GetUtcNow()), expectedRevision, cancellationToken).ConfigureAwait(false));
    }

    public async Task<PlannerResult<PlannerTaskDto>> CompleteTaskAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return Cancelled<PlannerTaskDto>();
        if (id == Guid.Empty) return Invalid<PlannerTaskDto>("Укажите ID задачи.");
        return Map(await store.ChangeAsync(id, task => task.Complete(clock.GetUtcNow()), null, cancellationToken).ConfigureAwait(false));
    }

    public async Task<PlannerResult<PlannerTaskDto>> ReopenTaskAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return Cancelled<PlannerTaskDto>();
        if (id == Guid.Empty) return Invalid<PlannerTaskDto>("Укажите ID задачи.");
        return Map(await store.ChangeAsync(id, task => task.Reopen(clock.GetUtcNow()), null, cancellationToken).ConfigureAwait(false));
    }

    public Task<PlannerResult<bool>> DeleteTaskAsync(Guid id, long? expectedRevision = null, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return Task.FromResult(Cancelled<bool>());
        if (id == Guid.Empty || expectedRevision is < 1) return Task.FromResult(Invalid<bool>("Проверьте ID и версию задачи."));
        return store.DeleteAsync(id, expectedRevision, cancellationToken);
    }

    public async Task<PlannerResult<PlannerTaskDto>> GetTaskByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return Cancelled<PlannerTaskDto>();
        if (id == Guid.Empty) return Invalid<PlannerTaskDto>("Укажите ID задачи.");
        return Map(await store.FindAsync(id, cancellationToken).ConfigureAwait(false));
    }

    public async Task<PlannerResult<IReadOnlyList<PlannerTaskDto>>> GetTasksAsync(PlannerQuery? query = null, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return Cancelled<IReadOnlyList<PlannerTaskDto>>();
        query ??= new();
        if (!Enum.IsDefined(query.View) || !Enum.IsDefined(query.Sort) || !Enum.IsDefined(query.Direction))
            return Invalid<IReadOnlyList<PlannerTaskDto>>("Неизвестный фильтр или порядок сортировки.");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), preferredTimeZone is null ? Kyiv : AppTimeZones.Get(preferredTimeZone())).DateTime);
        var result = await store.QueryAsync(query, today, cancellationToken).ConfigureAwait(false);
        return result.Success ? PlannerResult<IReadOnlyList<PlannerTaskDto>>.Ok(result.Value!.Select(t => t.ToDto()).ToArray())
            : PlannerResult<IReadOnlyList<PlannerTaskDto>>.Fail(result.Error, result.Message!);
    }
}
