using ZARRAR.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZARRAR.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260912100000_AddDynamicFlagHmacNonce")]
    public partial class AddDynamicFlagHmacNonce : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FlagInstanceId",
                table: "FlagContexts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FlagContexts_FlagInstanceId",
                table: "FlagContexts",
                column: "FlagInstanceId",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FlagContexts_FlagInstanceId",
                table: "FlagContexts");

            migrationBuilder.DropColumn(
                name: "FlagInstanceId",
                table: "FlagContexts");
        }
    }
}
