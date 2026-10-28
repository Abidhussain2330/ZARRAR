using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BURAQ.Migrations
{
    /// <inheritdoc />
    public partial class AddSoc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SocAlerts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SourceKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Event = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RiskImpact = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TeamId = table.Column<int>(type: "integer", nullable: true),
                    RelatedTeamId = table.Column<int>(type: "integer", nullable: true),
                    GameId = table.Column<int>(type: "integer", nullable: true),
                    ChallengeId = table.Column<int>(type: "integer", nullable: true),
                    InstanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubmissionId = table.Column<int>(type: "integer", nullable: true),
                    EvidenceFingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Evidence = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AssignedAdminId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocAlerts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SocEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TimestampUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EventType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Details = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    TeamId = table.Column<int>(type: "integer", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    GameId = table.Column<int>(type: "integer", nullable: true),
                    ChallengeId = table.Column<int>(type: "integer", nullable: true),
                    InstanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubmissionId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SocAlerts_Category_Severity_Status",
                table: "SocAlerts",
                columns: new[] { "Category", "Severity", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SocAlerts_CreatedAtUtc",
                table: "SocAlerts",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SocAlerts_SourceKey",
                table: "SocAlerts",
                column: "SourceKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SocAlerts_TeamId_Status_CreatedAtUtc",
                table: "SocAlerts",
                columns: new[] { "TeamId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SocEvents_Category_EventType_TimestampUtc",
                table: "SocEvents",
                columns: new[] { "Category", "EventType", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SocEvents_TeamId_TimestampUtc",
                table: "SocEvents",
                columns: new[] { "TeamId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SocEvents_TimestampUtc",
                table: "SocEvents",
                column: "TimestampUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SocAlerts");

            migrationBuilder.DropTable(
                name: "SocEvents");
        }
    }
}
