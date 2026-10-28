using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BURAQ.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261011150000_IncreaseTeamNameLengthTo40")]
public partial class IncreaseTeamNameLengthTo40 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "Name",
            table: "Teams",
            type: "character varying(40)",
            maxLength: 40,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(20)",
            oldMaxLength: 20);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "Name",
            table: "Teams",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(40)",
            oldMaxLength: 40);
    }
}
