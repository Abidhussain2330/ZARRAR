using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BURAQ.Services.HealthCheck;

public class DatabaseHealthCheck(AppDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var time = DateTime.UtcNow;
            var appliedMigrations =
                await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken);
            var knownMigrations = dbContext.Database.GetMigrations();
            return DateTime.UtcNow - time > TimeSpan.FromSeconds(1)
                ? HealthCheckResult.Degraded()
                : !IsMigrationStateHealthy(appliedMigrations, knownMigrations)
                    ? HealthCheckResult.Unhealthy()
                    : HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(exception: ex);
        }
    }

    // A database can legitimately contain migrations from a later compatible
    // application build during a controlled rollback. This binary is healthy
    // when every migration it requires has been applied; extra applied
    // migrations do not make the database unavailable.
    internal static bool IsMigrationStateHealthy(IEnumerable<string> appliedMigrations,
        IEnumerable<string> knownMigrations)
    {
        var applied = appliedMigrations.ToHashSet(StringComparer.Ordinal);
        return knownMigrations.All(applied.Contains);
    }
}
