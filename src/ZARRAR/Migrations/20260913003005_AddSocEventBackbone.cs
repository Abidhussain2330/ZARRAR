using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ZARRAR.Migrations
{
    /// <inheritdoc />
    public partial class AddSocEventBackbone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Details",
                table: "SocEvents",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                table: "SocEvents",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EventId",
                table: "SocEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayloadJson",
                table: "SocEvents",
                type: "character varying(8000)",
                maxLength: 8000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RelatedTeamId",
                table: "SocEvents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "SocEvents",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SocEventOutbox",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Severity = table.Column<byte>(type: "smallint", nullable: false),
                    Category = table.Column<byte>(type: "smallint", nullable: false),
                    EventType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Source = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Details = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PayloadJson = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    TeamId = table.Column<int>(type: "integer", nullable: true),
                    RelatedTeamId = table.Column<int>(type: "integer", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    GameId = table.Column<int>(type: "integer", nullable: true),
                    ChallengeId = table.Column<int>(type: "integer", nullable: true),
                    InstanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubmissionId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocEventOutbox", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SocEvents_EventId",
                table: "SocEvents",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SocEvents_GameId_TimestampUtc",
                table: "SocEvents",
                columns: new[] { "GameId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SocEventOutbox_EventId",
                table: "SocEventOutbox",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SocEventOutbox_GameId_OccurredAtUtc",
                table: "SocEventOutbox",
                columns: new[] { "GameId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SocEventOutbox_ProcessedAtUtc_NextAttemptAtUtc_Id",
                table: "SocEventOutbox",
                columns: new[] { "ProcessedAtUtc", "NextAttemptAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SocEventOutbox");

            migrationBuilder.DropIndex(
                name: "IX_SocEvents_EventId",
                table: "SocEvents");

            migrationBuilder.DropIndex(
                name: "IX_SocEvents_GameId_TimestampUtc",
                table: "SocEvents");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "SocEvents");

            migrationBuilder.DropColumn(
                name: "EventId",
                table: "SocEvents");

            migrationBuilder.DropColumn(
                name: "PayloadJson",
                table: "SocEvents");

            migrationBuilder.DropColumn(
                name: "RelatedTeamId",
                table: "SocEvents");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "SocEvents");

            migrationBuilder.AlterColumn<string>(
                name: "Details",
                table: "SocEvents",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000);
        }
    }
}
