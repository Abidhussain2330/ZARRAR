using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BURAQ.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260913050000_AddSocRiskCorrelation")]
public partial class AddSocRiskCorrelation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SocDetectionJobs",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                SubmissionId = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<byte>(type: "smallint", nullable: false),
                AttemptCount = table.Column<int>(type: "integer", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SocDetectionJobs", x => x.Id);
                table.ForeignKey(
                    name: "FK_SocDetectionJobs_Submissions_SubmissionId",
                    column: x => x.SubmissionId,
                    principalTable: "Submissions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "SocRiskCases",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CaseKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                Summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                RiskScore = table.Column<int>(type: "integer", nullable: false),
                Confidence = table.Column<int>(type: "integer", nullable: false),
                RuleHitCount = table.Column<int>(type: "integer", nullable: false),
                EvidenceCount = table.Column<int>(type: "integer", nullable: false),
                FirstSeenAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                LastSeenAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                GameId = table.Column<int>(type: "integer", nullable: false),
                PrimaryTeamId = table.Column<int>(type: "integer", nullable: false),
                RelatedTeamId = table.Column<int>(type: "integer", nullable: true),
                AssignedAdminId = table.Column<Guid>(type: "uuid", nullable: true),
                Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SocRiskCases", x => x.Id);
                table.ForeignKey(
                    name: "FK_SocRiskCases_AspNetUsers_AssignedAdminId",
                    column: x => x.AssignedAdminId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_SocRiskCases_Games_GameId",
                    column: x => x.GameId,
                    principalTable: "Games",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_SocRiskCases_Teams_PrimaryTeamId",
                    column: x => x.PrimaryTeamId,
                    principalTable: "Teams",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_SocRiskCases_Teams_RelatedTeamId",
                    column: x => x.RelatedTeamId,
                    principalTable: "Teams",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "SocRiskEvidence",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                RiskCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                EvidenceKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Weight = table.Column<int>(type: "integer", nullable: false),
                Confidence = table.Column<int>(type: "integer", nullable: false),
                OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                TeamId = table.Column<int>(type: "integer", nullable: true),
                RelatedTeamId = table.Column<int>(type: "integer", nullable: true),
                ChallengeId = table.Column<int>(type: "integer", nullable: true),
                SubmissionId = table.Column<int>(type: "integer", nullable: true),
                AlertId = table.Column<long>(type: "bigint", nullable: true),
                Fingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                MetadataJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SocRiskEvidence", x => x.Id);
                table.ForeignKey(
                    name: "FK_SocRiskEvidence_SocAlerts_AlertId",
                    column: x => x.AlertId,
                    principalTable: "SocAlerts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
                table.ForeignKey(
                    name: "FK_SocRiskEvidence_SocRiskCases_RiskCaseId",
                    column: x => x.RiskCaseId,
                    principalTable: "SocRiskCases",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_SocRiskEvidence_Submissions_SubmissionId",
                    column: x => x.SubmissionId,
                    principalTable: "Submissions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(name: "IX_SocDetectionJobs_Status_CreatedAtUtc",
            table: "SocDetectionJobs", columns: new[] { "Status", "CreatedAtUtc" });
        migrationBuilder.CreateIndex(name: "IX_SocDetectionJobs_SubmissionId",
            table: "SocDetectionJobs", column: "SubmissionId", unique: true);
        migrationBuilder.CreateIndex(name: "IX_SocRiskCases_AssignedAdminId",
            table: "SocRiskCases", column: "AssignedAdminId");
        migrationBuilder.CreateIndex(name: "IX_SocRiskCases_CaseKey",
            table: "SocRiskCases", column: "CaseKey", unique: true);
        migrationBuilder.CreateIndex(name: "IX_SocRiskCases_GameId_PrimaryTeamId_RelatedTeamId",
            table: "SocRiskCases", columns: new[] { "GameId", "PrimaryTeamId", "RelatedTeamId" });
        migrationBuilder.CreateIndex(name: "IX_SocRiskCases_PrimaryTeamId",
            table: "SocRiskCases", column: "PrimaryTeamId");
        migrationBuilder.CreateIndex(name: "IX_SocRiskCases_RelatedTeamId",
            table: "SocRiskCases", column: "RelatedTeamId");
        migrationBuilder.CreateIndex(name: "IX_SocRiskCases_Status_RiskScore_UpdatedAtUtc",
            table: "SocRiskCases", columns: new[] { "Status", "RiskScore", "UpdatedAtUtc" });
        migrationBuilder.CreateIndex(name: "IX_SocRiskEvidence_AlertId",
            table: "SocRiskEvidence", column: "AlertId");
        migrationBuilder.CreateIndex(name: "IX_SocRiskEvidence_EvidenceKey",
            table: "SocRiskEvidence", column: "EvidenceKey", unique: true);
        migrationBuilder.CreateIndex(name: "IX_SocRiskEvidence_RiskCaseId_OccurredAtUtc",
            table: "SocRiskEvidence", columns: new[] { "RiskCaseId", "OccurredAtUtc" });
        migrationBuilder.CreateIndex(name: "IX_SocRiskEvidence_SubmissionId",
            table: "SocRiskEvidence", column: "SubmissionId");

        migrationBuilder.Sql(
            """
            INSERT INTO "SocDetectionJobs"
                ("SubmissionId", "Status", "AttemptCount", "CreatedAtUtc")
            SELECT s."Id", 0, 0, NOW()
            FROM "Submissions" s
            WHERE s."Status" = 'CheatDetected'
               OR s."SubmitTimeUtc" >= NOW() - INTERVAL '7 days'
            ON CONFLICT ("SubmissionId") DO NOTHING;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "SocDetectionJobs");
        migrationBuilder.DropTable(name: "SocRiskEvidence");
        migrationBuilder.DropTable(name: "SocRiskCases");
    }
}
