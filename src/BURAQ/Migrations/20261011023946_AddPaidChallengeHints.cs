using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BURAQ.Migrations
{
    /// <inheritdoc />
    public partial class AddPaidChallengeHints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HintPenalties",
                table: "GameChallenges",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HintUnlocks",
                columns: table => new
                {
                    ParticipationId = table.Column<int>(type: "integer", nullable: false),
                    ChallengeId = table.Column<int>(type: "integer", nullable: false),
                    HintIndex = table.Column<int>(type: "integer", nullable: false),
                    Penalty = table.Column<int>(type: "integer", nullable: false),
                    UnlockedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HintUnlocks", x => new { x.ParticipationId, x.ChallengeId, x.HintIndex });
                    table.ForeignKey(
                        name: "FK_HintUnlocks_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_HintUnlocks_GameChallenges_ChallengeId",
                        column: x => x.ChallengeId,
                        principalTable: "GameChallenges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_HintUnlocks_Participations_ParticipationId",
                        column: x => x.ParticipationId,
                        principalTable: "Participations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HintUnlocks_ChallengeId",
                table: "HintUnlocks",
                column: "ChallengeId");

            migrationBuilder.CreateIndex(
                name: "IX_HintUnlocks_UserId",
                table: "HintUnlocks",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HintUnlocks");

            migrationBuilder.DropColumn(
                name: "HintPenalties",
                table: "GameChallenges");

        }
    }
}
