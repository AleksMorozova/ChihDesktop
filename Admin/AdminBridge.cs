using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChihDesktop;

public sealed record AdminRequest
{
    public required int ProtocolVersion { get; init; }
    public required string RequestId { get; init; }
    public required string Operation { get; init; }
    public required JsonElement Payload { get; init; }
}
public sealed record AdminError(string Code, string Message);
public sealed record AdminResponse(int ProtocolVersion, string? RequestId, string Operation, bool Success, object? Data, AdminError? Error);

// One instance per WebView session. No transport, UI, EF, host objects, or payload logging.
public sealed class AdminBridge(IPlannerService planner, Action? openSettings = null, AdminApplicationApi? application = null) : IDisposable
{
    public const int Version = 1, MaxInFlight = 8, MaxRequestsPerSession = 4096, MaxMessageLength = 65536;
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.Strict,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
        MaxDepth = 16
    };
    private readonly object sync = new();
    private readonly HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource lifetime = new();
    private int active;
    private bool disposed;
    private static readonly HashSet<string> Operations = new(new[] { "planner.reminder", "planner.resolveTime", "planner.list", "planner.get", "planner.create", "planner.update", "planner.complete", "planner.reopen", "planner.delete", "settings.open" }.Concat(AdminApplicationApi.Operations));

    public static string Serialize(AdminResponse response) => JsonSerializer.Serialize(response, JsonOptions);
    private static AdminResponse Fail(string? id, string op, string code, string message) => new(Version, id, op, false, null, new(code, message));
    private static AdminResponse Map<T>(AdminRequest request, PlannerResult<T> result) => result.Success
        ? new(Version, request.RequestId, request.Operation, true, result.Value, null)
        : Fail(request.RequestId, request.Operation, result.Error.ToString(), result.Message ?? "Не удалось выполнить действие.");

    public async Task<AdminResponse> HandleAsync(string json, CancellationToken cancellationToken = default)
    {
        AdminRequest request;
        try
        {
            if (json.Length > MaxMessageLength) return Fail(null, "", "InvalidMessage", "Запрос слишком большой.");
            using var document = JsonDocument.Parse(json, new() { MaxDepth = 16 });
            RejectDuplicateProperties(document.RootElement);
            request = document.RootElement.Deserialize<AdminRequest>(JsonOptions) ?? throw new JsonException();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        { return Fail(null, "", "InvalidMessage", "Некорректный формат запроса."); }
        if (!Guid.TryParseExact(request.RequestId, "D", out _)) return Fail(null, request.Operation, "InvalidMessage", "Некорректный идентификатор запроса.");
        if (request.ProtocolVersion != Version) return Fail(request.RequestId, request.Operation, "VersionMismatch", "Обновите интерфейс и приложение до одной версии.");
        if (request.Operation is null || !Operations.Contains(request.Operation)) return Fail(request.RequestId, request.Operation ?? "", "InvalidOperation", "Операция не разрешена.");
        CancellationTokenSource timeout;
        lock (sync)
        {
            if (disposed) return Fail(request.RequestId, request.Operation, "Disposed", "Окно закрыто.");
            if (seen.Contains(request.RequestId)) return Fail(request.RequestId, request.Operation, "DuplicateRequest", "Запрос уже обработан. Обновите список.");
            if (seen.Count >= MaxRequestsPerSession) return Fail(request.RequestId, request.Operation, "SessionLimit", "Закройте и снова откройте окно планировщика.");
            if (active >= MaxInFlight) return Fail(request.RequestId, request.Operation, "Busy", "Слишком много запросов. Повторите позже.");
            seen.Add(request.RequestId); active++;
            timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
        }
        using (timeout)
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            var token = timeout.Token;
            try
            {
                token.ThrowIfCancellationRequested();
                if (AdminApplicationApi.Operations.Contains(request.Operation))
                    return application is null ? Fail(request.RequestId, request.Operation, "Unavailable", "Нативная служба недоступна. Откройте настройки из меню Чиха.")
                        : new(Version, request.RequestId, request.Operation, true, application.Execute(request.Operation, request.Payload), null);
                return request.Operation switch
                {
                    "settings.open" => OpenSettings(request),
                    "planner.resolveTime" => ResolveTime(request),
                    "planner.reminder" => await Reminder(request, token).ConfigureAwait(false),
                    "planner.list" => Map(request, await planner.GetTasksAsync(Read<QueryPayload>(request).Query, token).ConfigureAwait(false)),
                    "planner.get" => Map(request, await planner.GetTaskByIdAsync(Read<IdPayload>(request).Id, token).ConfigureAwait(false)),
                    "planner.create" => Map(request, await planner.CreateTaskAsync(Read<CreatePayload>(request).Task, token).ConfigureAwait(false)),
                    "planner.update" => await Update(request, token).ConfigureAwait(false),
                    "planner.complete" => Map(request, await planner.CompleteTaskAsync(Read<IdPayload>(request).Id, token).ConfigureAwait(false)),
                    "planner.reopen" => Map(request, await planner.ReopenTaskAsync(Read<IdPayload>(request).Id, token).ConfigureAwait(false)),
                    "planner.delete" => await Delete(request, token).ConfigureAwait(false),
                    _ => throw new InvalidOperationException()
                };
            }
            catch (AdminCommandException ex) { return Fail(request.RequestId, request.Operation, ex.Code, ex.Message); }
            catch (JsonException) { return Fail(request.RequestId, request.Operation, "InvalidPayload", "Проверьте поля запроса, дату YYYY-MM-DD и время HH:mm."); }
            catch (OperationCanceledException) { return Fail(request.RequestId, request.Operation, "Cancelled", "Ожидание отменено. Обновите список перед повтором действия."); }
            catch (Exception) { return Fail(request.RequestId, request.Operation, "InternalError", "Не удалось выполнить действие. Обновите список; если ошибка повторяется, перезапустите окно."); }
            finally { lock (sync) active--; }
        }
    }
    private static AdminResponse ResolveTime(AdminRequest request)
    {
        var input = Read<TimePayload>(request);
        try { return new(Version, request.RequestId, request.Operation, true, TaskReminderTime.Resolve(input.Local, input.TimeZoneId), null); }
        catch (Exception ex) when (ex is ArgumentException or TimeZoneNotFoundException or InvalidTimeZoneException)
        { return Fail(request.RequestId, request.Operation, "InvalidPayload", "Проверьте часовой пояс и время; время перевода часов не должно быть неоднозначным."); }
    }
    private async Task<AdminResponse> Reminder(AdminRequest request, CancellationToken token)
    {
        var input = Read<ReminderPayload>(request);
        if (input.Action is not ("snooze" or "dismiss") || planner is not ITaskReminderCommands commands)
            return Fail(request.RequestId, request.Operation, "InvalidPayload", "Действие недоступно.");
        return Map(request, await commands.ChangeReminderAsync(input.Id, input.Action, input.ExpectedRevision, token).ConfigureAwait(false));
    }
    private sealed record TimePayload { public required string Local { get; init; } public required string TimeZoneId { get; init; } }
    private sealed record ReminderPayload { public required Guid Id { get; init; } public required string Action { get; init; } public required long ExpectedRevision { get; init; } }

    private AdminResponse OpenSettings(AdminRequest request)
    {
        _ = Read<EmptyPayload>(request);
        if (openSettings is null) return Fail(request.RequestId, request.Operation, "Unavailable", "Настройки доступны из меню кота.");
        openSettings(); return new(Version, request.RequestId, request.Operation, true, true, null);
    }
    private sealed record EmptyPayload;

    private async Task<AdminResponse> Update(AdminRequest request, CancellationToken token)
    {
        var payload = Read<UpdatePayload>(request);
        return Map(request, await planner.UpdateTaskAsync(payload.Id, payload.Task, payload.ExpectedRevision, token).ConfigureAwait(false));
    }
    private async Task<AdminResponse> Delete(AdminRequest request, CancellationToken token)
    {
        var payload = Read<DeletePayload>(request);
        return Map(request, await planner.DeleteTaskAsync(payload.Id, payload.ExpectedRevision, token).ConfigureAwait(false));
    }
    private static T Read<T>(AdminRequest request)
    {
        if (request.Payload.ValueKind != JsonValueKind.Object) throw new JsonException();
        if (request.Payload.TryGetProperty("task", out var task) && task.ValueKind == JsonValueKind.Object &&
            task.TryGetProperty("remindAtUtc", out var remind) && remind.ValueKind != JsonValueKind.Null &&
            (remind.ValueKind != JsonValueKind.String || !System.Text.RegularExpressions.Regex.IsMatch(remind.GetString()!, @"(Z|[+-]\d{2}:\d{2})$")))
            throw new JsonException();
        var value = request.Payload.Deserialize<T>(JsonOptions) ?? throw new JsonException();
        if (value is CreatePayload { Task: null } or UpdatePayload { Task: null } or QueryPayload { Query: null }) throw new JsonException();
        return value;
    }
    private sealed record IdPayload { public required Guid Id { get; init; } }
    private sealed record CreatePayload { public required PlannerTaskInput Task { get; init; } }
    private sealed record UpdatePayload { public required Guid Id { get; init; } public required PlannerTaskInput Task { get; init; } public required long ExpectedRevision { get; init; } }
    private sealed record DeletePayload { public required Guid Id { get; init; } public required long ExpectedRevision { get; init; } }
    private sealed record QueryPayload { public required PlannerQuery Query { get; init; } }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException();
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
    }
    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            lifetime.Cancel(); lifetime.Dispose();
        }
    }
}
