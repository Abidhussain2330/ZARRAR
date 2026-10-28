using System.Reflection;
using BURAQ.Models;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace BURAQ.Test;

public sealed class UsernameLengthMigrationTests
{
    [Fact]
    public void Migration_expands_existing_usernames_to_40_characters()
    {
        var migrationType = typeof(AppDbContext).Assembly.GetType(
            "BURAQ.Migrations.IncreaseUserNameLengthTo40");

        Assert.NotNull(migrationType);

        var migration = Assert.IsAssignableFrom<Migration>(Activator.CreateInstance(migrationType!)!);
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        var up = migrationType!.GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(up);
        up!.Invoke(migration, [builder]);

        var userNameOperation = Assert.Single(builder.Operations.OfType<AlterColumnOperation>(),
            operation => operation.Table == "AspNetUsers" && operation.Name == "UserName");
        var logUserNameOperation = Assert.Single(builder.Operations.OfType<AlterColumnOperation>(),
            operation => operation.Table == "Logs" && operation.Name == "UserName");

        Assert.Equal(40, userNameOperation.MaxLength);
        Assert.Equal("character varying(40)", userNameOperation.ColumnType);
        Assert.Equal(40, logUserNameOperation.MaxLength);
        Assert.Equal("character varying(40)", logUserNameOperation.ColumnType);
    }
}
