using Bookly.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bookly.Core;

/// <summary>
/// The Core module's single entry point. The API composition root calls this;
/// nothing outside the module ever news up Core services directly.
/// </summary>
public static class CoreModule
{
    public static IServiceCollection AddCoreModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<CoreDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("BooklyDb"),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", CoreDbContext.Schema)));

        return services;
    }

    /// <summary>
    /// Dev-only convenience: apply pending migrations on startup.
    /// Production will run migrations as an explicit deploy step (ADR-012, decided in M10).
    /// </summary>
    public static async Task ApplyCoreMigrationsAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        await dbContext.Database.MigrateAsync();
    }
}
