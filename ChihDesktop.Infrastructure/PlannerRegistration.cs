using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ChihDesktop.Infrastructure;

public static class PlannerRegistration
{
    public static IServiceCollection AddPlannerStorage(this IServiceCollection services, PlannerDatabaseOptions options)
    {
        services.AddSingleton(options);
        services.AddDbContextFactory<PlannerDbContext>(builder => builder.UseSqlite(options.ConnectionString));
        services.AddSingleton<PlannerDatabaseExecutor>();
        services.AddSingleton<IPlannerDatabaseInitializer>(provider => provider.GetRequiredService<PlannerDatabaseExecutor>());
        services.AddSingleton<IPlannerTaskStore, SqlitePlannerTaskStore>();
        services.AddSingleton<IPlannerService, PlannerService>();
        return services;
    }
}
