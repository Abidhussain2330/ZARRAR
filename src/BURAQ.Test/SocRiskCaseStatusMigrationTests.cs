using System.Reflection;
using BURAQ.Models;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace BURAQ.Test;

public sealed class SocRiskCaseStatusMigrationTests
{
    [Fact]
    public void StatusMigrationConvertsLegacySmallintColumnsToTextValues()
    {
        var migrationType = typeof(AppDbContext).Assembly.GetType(
            "BURAQ.Migrations.ConvertSocRiskCaseStatusToText");

        Assert.NotNull(migrationType);

        var migration = Assert.IsAssignableFrom<Migration>(Activator.CreateInstance(migrationType!)!);
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        var up = migrationType!.GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(up);
        up!.Invoke(migration, [builder]);

        var sql = Assert.Single(builder.Operations.OfType<SqlOperation>()).Sql;
        Assert.Contains("\"SocRiskCases\"", sql);
        Assert.Contains("udt_name = 'int2'", sql);
        Assert.Contains("WHEN 0 THEN 'Open'", sql);
        Assert.Contains("WHEN 4 THEN 'Escalated'", sql);
        Assert.Contains("character varying(20)", sql);
    }
}
