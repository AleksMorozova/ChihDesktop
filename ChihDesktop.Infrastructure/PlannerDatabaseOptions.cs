using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ChihDesktop.Infrastructure;

public sealed record PlannerDatabaseOptions
{
    public string DatabasePath { get; }
    public PlannerDatabaseOptions(string? databasePath = null) => DatabasePath = Path.GetFullPath(databasePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChihDesktop", "chih.db"));

    public string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = DatabasePath, Mode = SqliteOpenMode.ReadWriteCreate,
        Pooling = false, DefaultTimeout = 5, ForeignKeys = true
    }.ToString();
}

// EF tooling never starts WPF and must be given an explicit development DB path.
public sealed class PlannerDesignTimeFactory : IDesignTimeDbContextFactory<PlannerDbContext>
{
    public PlannerDbContext CreateDbContext(string[] args)
    {
        int index = Array.IndexOf(args, "--database-path");
        if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("Supply --database-path <development path>; no user DB is selected by tooling.");
        var options = new PlannerDatabaseOptions(args[index + 1]);
        return new(new DbContextOptionsBuilder<PlannerDbContext>().UseSqlite(options.ConnectionString).Options);
    }
}
