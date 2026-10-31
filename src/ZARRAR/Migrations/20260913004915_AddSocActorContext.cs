using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZARRAR.Migrations
{
    /// <inheritdoc />
    public partial class AddSocActorContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ActorUserId",
                table: "SocEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ActorUserId",
                table: "SocEventOutbox",
                type: "uuid",
                nullable: true);

            // Before Phase 02, SocEvents.UserId represented the administrator/actor for the
            // platform-security events that populated it. Preserve that attribution explicitly.
            // UserId remains untouched because older consumers may still rely on it.
            migrationBuilder.Sql(
                """
                UPDATE "SocEvents"
                SET "ActorUserId" = "UserId"
                WHERE "ActorUserId" IS NULL
                  AND "UserId" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActorUserId",
                table: "SocEvents");

            migrationBuilder.DropColumn(
                name: "ActorUserId",
                table: "SocEventOutbox");
        }
    }
}
