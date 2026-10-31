using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZARRAR.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260912140000_AddParticipationContainerLimit")]
public partial class AddParticipationContainerLimit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "ContainerCountLimit",
            table: "Participations",
            type: "integer",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ContainerCountLimit",
            table: "Participations");
    }
}
