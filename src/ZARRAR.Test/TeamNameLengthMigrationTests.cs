using System.Reflection;
using ZARRAR.Models;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace ZARRAR.Test;

public sealed class TeamNameLengthMigrationTests
{
    [Fact]
    public void Team_names_support_up_to_40_characters_in_the_application_and_database()
    {
        Assert.Equal(40, Limits.MaxTeamNameLength);

        var migrationType = typeof(AppDbContext).Assembly.GetType(
            "ZARRAR.Migrations.IncreaseTeamNameLengthTo40");

        Assert.NotNull(migrationType);

        var migration = Assert.IsAssignableFrom<Migration>(Activator.CreateInstance(migrationType!)!);
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        var up = migrationType!.GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(up);
        up!.Invoke(migration, [builder]);

        var nameOperation = Assert.Single(builder.Operations.OfType<AlterColumnOperation>(),
            operation => operation.Table == "Teams" && operation.Name == "Name");

        Assert.Equal(40, nameOperation.MaxLength);
        Assert.Equal("character varying(40)", nameOperation.ColumnType);
    }
}
