namespace ChihDesktop;

public enum PlannerTaskStatus { Active, Completed }
public enum PlannerPriority { Low, Normal, High }
public enum PlannerCategory { Personal, Study, Work }
public enum PlannerView { Inbox, Today, Upcoming, Completed, AllActive }
public enum PlannerSort { DueDate, Priority, CreatedAt }
public enum PlannerSortDirection { Ascending, Descending }

public sealed record PlannerTaskInput(string Title, string? Description = null,
    PlannerPriority Priority = PlannerPriority.Normal, PlannerCategory Category = PlannerCategory.Personal, DateOnly? DueDate = null)
{
    public PlannerTaskInput Validate()
    {
        if (string.IsNullOrWhiteSpace(Title) || Title.Trim().Length > 200)
            throw new ArgumentException("Название обязательно и не должно превышать 200 символов.");
        if (!Enum.IsDefined(Priority) || !Enum.IsDefined(Category))
            throw new ArgumentException("Неизвестный приоритет или категория.");
        return this with { Title = Title.Trim() };
    }
}

public sealed record PlannerQuery(PlannerView View = PlannerView.AllActive, PlannerSort Sort = PlannerSort.DueDate,
    PlannerSortDirection Direction = PlannerSortDirection.Ascending);

// Read-only application contract; neither this DTO nor the domain entity is an EF-tracked object.
public sealed record PlannerTaskDto(Guid Id, string Title, string? Description, PlannerTaskStatus Status,
    PlannerPriority Priority, PlannerCategory Category, DateOnly? DueDate, DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc, DateTimeOffset? CompletedAtUtc, long Revision);

public sealed class PlannerTask
{
    public Guid Id { get; private set; }
    public string Title { get; private set; } = "";
    public string? Description { get; private set; }
    public PlannerTaskStatus Status { get; private set; }
    public PlannerPriority Priority { get; private set; }
    public PlannerCategory Category { get; private set; }
    public DateOnly? DueDate { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public long Revision { get; private set; }
    private PlannerTask() { }

    public static PlannerTask Create(PlannerTaskInput input, DateTimeOffset now)
    {
        input = input.Validate();
        return new() { Id = Guid.NewGuid(), Title = input.Title, Description = input.Description,
            Priority = input.Priority, Category = input.Category, DueDate = input.DueDate,
            CreatedAtUtc = now.ToUniversalTime(), UpdatedAtUtc = now.ToUniversalTime(), Revision = 1 };
    }

    public bool Update(PlannerTaskInput input, DateTimeOffset now)
    {
        input = input.Validate();
        if (Title == input.Title && Description == input.Description && Priority == input.Priority &&
            Category == input.Category && DueDate == input.DueDate) return false;
        Title = input.Title; Description = input.Description; Priority = input.Priority;
        Category = input.Category; DueDate = input.DueDate;
        Touch(now);
        return true;
    }

    public bool Complete(DateTimeOffset now)
    {
        if (Status == PlannerTaskStatus.Completed) return false;
        Status = PlannerTaskStatus.Completed; CompletedAtUtc = now.ToUniversalTime(); Touch(now);
        return true;
    }

    public bool Reopen(DateTimeOffset now)
    {
        if (Status == PlannerTaskStatus.Active) return false;
        Status = PlannerTaskStatus.Active; CompletedAtUtc = null; Touch(now);
        return true;
    }

    private void Touch(DateTimeOffset now) { UpdatedAtUtc = now.ToUniversalTime(); Revision = checked(Revision + 1); }

    public PlannerTaskDto ToDto() => new(Id, Title, Description, Status, Priority, Category, DueDate,
        CreatedAtUtc, UpdatedAtUtc, CompletedAtUtc, Revision);

    public static PlannerTask Restore(PlannerTaskDto value)
    {
        _ = new PlannerTaskInput(value.Title, value.Description, value.Priority, value.Category, value.DueDate).Validate();
        if (value.Id == Guid.Empty || value.Revision < 1 || !Enum.IsDefined(value.Status) ||
            (value.Status == PlannerTaskStatus.Completed) != value.CompletedAtUtc.HasValue)
            throw new ArgumentException("Некорректная сохранённая задача.");
        return new() { Id = value.Id, Title = value.Title, Description = value.Description, Status = value.Status,
            Priority = value.Priority, Category = value.Category, DueDate = value.DueDate,
            CreatedAtUtc = value.CreatedAtUtc.ToUniversalTime(), UpdatedAtUtc = value.UpdatedAtUtc.ToUniversalTime(),
            CompletedAtUtc = value.CompletedAtUtc?.ToUniversalTime(), Revision = value.Revision };
    }
}
