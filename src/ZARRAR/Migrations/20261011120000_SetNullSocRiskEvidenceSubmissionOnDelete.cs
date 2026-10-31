using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZARRAR.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261011120000_SetNullSocRiskEvidenceSubmissionOnDelete")]
public partial class SetNullSocRiskEvidenceSubmissionOnDelete : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_SocRiskEvidence_Submissions_SubmissionId",
            table: "SocRiskEvidence");

        migrationBuilder.AddForeignKey(
            name: "FK_SocRiskEvidence_Submissions_SubmissionId",
            table: "SocRiskEvidence",
            column: "SubmissionId",
            principalTable: "Submissions",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_SocRiskEvidence_Submissions_SubmissionId",
            table: "SocRiskEvidence");

        migrationBuilder.AddForeignKey(
            name: "FK_SocRiskEvidence_Submissions_SubmissionId",
            table: "SocRiskEvidence",
            column: "SubmissionId",
            principalTable: "Submissions",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }
}
