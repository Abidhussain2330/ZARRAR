using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BURAQ.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261011090000_IncreaseUserNameLengthTo40")]
public partial class IncreaseUserNameLengthTo40 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "UserName",
            table: "AspNetUsers",
            type: "character varying(40)",
            maxLength: 40,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(16)",
            oldMaxLength: 16,
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "UserName",
            table: "Logs",
            type: "character varying(40)",
            maxLength: 40,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(15)",
            oldMaxLength: 15,
            oldNullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "UserName",
            table: "Logs",
            type: "character varying(15)",
            maxLength: 15,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(40)",
            oldMaxLength: 40,
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "UserName",
            table: "AspNetUsers",
            type: "character varying(16)",
            maxLength: 16,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(40)",
            oldMaxLength: 40,
            oldNullable: true);
    }
}
