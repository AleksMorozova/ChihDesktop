using Microsoft.EntityFrameworkCore;

namespace ChihDesktop.Infrastructure;

// Storage rows are private to Infrastructure; application APIs return Core DTOs.
internal sealed class PlannerTaskRow
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public int Status { get; set; }
    public int Priority { get; set; }
    public int Category { get; set; }
    public DateOnly? DueDate { get; set; }
    public long CreatedAtUtcTicks { get; set; }
    public long UpdatedAtUtcTicks { get; set; }
    public long? CompletedAtUtcTicks { get; set; }
    public long Revision { get; set; }

    public PlannerTask ToDomain() => PlannerTask.Restore(new(Id, Title, Description, (PlannerTaskStatus)Status,
        (PlannerPriority)Priority, (PlannerCategory)Category, DueDate, new(CreatedAtUtcTicks, TimeSpan.Zero),
        new(UpdatedAtUtcTicks, TimeSpan.Zero), CompletedAtUtcTicks is { } ticks ? new DateTimeOffset(ticks, TimeSpan.Zero) : null, Revision));

    public void Apply(PlannerTask task)
    {
        Id = task.Id; Title = task.Title; Description = task.Description; Status = (int)task.Status;
        Priority = (int)task.Priority; Category = (int)task.Category; DueDate = task.DueDate;
        CreatedAtUtcTicks = task.CreatedAtUtc.UtcTicks; UpdatedAtUtcTicks = task.UpdatedAtUtc.UtcTicks;
        CompletedAtUtcTicks = task.CompletedAtUtc?.UtcTicks; Revision = task.Revision;
    }
}

public sealed class PlannerDbContext(DbContextOptions<PlannerDbContext> options) : DbContext(options)
{
    internal DbSet<PlannerTaskRow> Tasks => Set<PlannerTaskRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var task = modelBuilder.Entity<PlannerTaskRow>();
        task.ToTable("PlannerTasks", table =>
        {
            table.HasCheckConstraint("CK_PlannerTasks_Title", "length(trim(\"Title\")) BETWEEN 1 AND 200");
            table.HasCheckConstraint("CK_PlannerTasks_Status", "\"Status\" IN (0, 1)");
            table.HasCheckConstraint("CK_PlannerTasks_Priority", "\"Priority\" IN (0, 1, 2)");
            table.HasCheckConstraint("CK_PlannerTasks_Category", "\"Category\" IN (0, 1, 2)");
            table.HasCheckConstraint("CK_PlannerTasks_Completion", "(\"Status\" = 0 AND \"CompletedAtUtcTicks\" IS NULL) OR (\"Status\" = 1 AND \"CompletedAtUtcTicks\" IS NOT NULL)");
            table.HasCheckConstraint("CK_PlannerTasks_Revision", "\"Revision\" >= 1");
        });
        task.HasKey(t => t.Id);
        task.Property(t => t.Title).IsRequired().HasMaxLength(200);
        task.Property(t => t.Revision).IsConcurrencyToken();
        task.HasIndex(t => new { t.Status, t.DueDate, t.Priority, t.CreatedAtUtcTicks });
        task.HasIndex(t => new { t.Status, t.CreatedAtUtcTicks });
    }
}
