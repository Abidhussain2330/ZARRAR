using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BURAQ.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261011100000_CascadeSocRiskCasesForTeamDeletion")]
public partial class CascadeSocRiskCasesForTeamDeletion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_SocRiskCases_Teams_PrimaryTeamId",
            table: "SocRiskCases");

        migrationBuilder.DropForeignKey(
            name: "FK_SocRiskCases_Teams_RelatedTeamId",
            table: "SocRiskCases");

        migrationBuilder.AddForeignKey(
            name: "FK_SocRiskCases_Teams_PrimaryTeamId",
            table: "SocRiskCases",
            column: "PrimaryTeamId",
            principalTable: "Teams",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        migrationBuilder.AddForeignKey(
            name: "FK_SocRiskCases_Teams_RelatedTeamId",
            table: "SocRiskCases",
            column: "RelatedTeamId",
            principalTable: "Teams",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_SocRiskCases_Teams_PrimaryTeamId",
            table: "SocRiskCases");

        migrationBuilder.DropForeignKey(
            name: "FK_SocRiskCases_Teams_RelatedTeamId",
            table: "SocRiskCases");

        migrationBuilder.AddForeignKey(
            name: "FK_SocRiskCases_Teams_PrimaryTeamId",
            table: "SocRiskCases",
            column: "PrimaryTeamId",
            principalTable: "Teams",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_SocRiskCases_Teams_RelatedTeamId",
            table: "SocRiskCases",
            column: "RelatedTeamId",
            principalTable: "Teams",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }
}
