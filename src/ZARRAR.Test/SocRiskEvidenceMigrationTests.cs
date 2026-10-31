using System.Reflection;
using ZARRAR.Models;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace ZARRAR.Test;

public sealed class SocRiskEvidenceMigrationTests
{
    [Fact]
    public void Submission_evidence_fk_is_migrated_to_set_null_on_submission_deletion()
    {
        var migrationType = typeof(AppDbContext).Assembly.GetType(
            "ZARRAR.Migrations.SetNullSocRiskEvidenceSubmissionOnDelete");

        Assert.NotNull(migrationType);

        var migration = Assert.IsAssignableFrom<Migration>(Activator.CreateInstance(migrationType!)!);
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        var up = migrationType!.GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(up);
        up!.Invoke(migration, [builder]);

        var foreignKey = Assert.Single(builder.Operations.OfType<AddForeignKeyOperation>());
        Assert.Equal("FK_SocRiskEvidence_Submissions_SubmissionId", foreignKey.Name);
        Assert.Equal("SocRiskEvidence", foreignKey.Table);
        Assert.Equal("SubmissionId", foreignKey.Columns.Single());
        Assert.Equal("Submissions", foreignKey.PrincipalTable);
        Assert.Equal(ReferentialAction.SetNull, foreignKey.OnDelete);
    }
}
