using System.Reflection;
using ZARRAR.Services.HealthCheck;
using Xunit;

namespace ZARRAR.Test;

public class DatabaseHealthCheckTests
{
    [Fact]
    public void Newer_applied_migrations_do_not_make_an_older_compatible_binary_unhealthy()
    {
        var method = typeof(DatabaseHealthCheck).GetMethod("IsMigrationStateHealthy",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var healthy = (bool)method!.Invoke(null,
            [new[] { "20260930", "20261009" }, new[] { "20260930" }])!;

        Assert.True(healthy);
    }

    [Fact]
    public void Missing_known_migration_keeps_database_unhealthy()
    {
        var method = typeof(DatabaseHealthCheck).GetMethod("IsMigrationStateHealthy",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var healthy = (bool)method!.Invoke(null,
            [new[] { "20260930" }, new[] { "20260930", "20261009" }])!;

        Assert.False(healthy);
    }
}
