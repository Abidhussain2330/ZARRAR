using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BURAQ.Migrations;

public partial class AddDeterministicVariantBuilder : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "VariantTemplates",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ChallengeId = table.Column<int>(type: "integer", nullable: false),
                Version = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                Kind = table.Column<byte>(type: "smallint", nullable: false),
                ManifestJson = table.Column<string>(type: "character varying(16000)", maxLength: 16000, nullable: false),
                ManifestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                BuilderImageDigest = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                OutputFileName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Status = table.Column<byte>(type: "smallint", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ValidatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_VariantTemplates", x => x.Id);
                table.ForeignKey(
                    name: "FK_VariantTemplates_GameChallenges_ChallengeId",
                    column: x => x.ChallengeId,
                    principalTable: "GameChallenges",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "VariantBuildJobs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TemplateId = table.Column<long>(type: "bigint", nullable: false),
                ParticipationId = table.Column<int>(type: "integer", nullable: false),
                FlagInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                VariantAssignmentId = table.Column<long>(type: "bigint", nullable: true),
                SeedCommitment = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Status = table.Column<byte>(type: "smallint", nullable: false),
                AttemptCount = table.Column<int>(type: "integer", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ArtifactHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                GeneratorVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_VariantBuildJobs", x => x.Id);
                table.ForeignKey(
                    name: "FK_VariantBuildJobs_Participations_ParticipationId",
                    column: x => x.ParticipationId,
                    principalTable: "Participations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_VariantBuildJobs_VariantAssignments_VariantAssignmentId",
                    column: x => x.VariantAssignmentId,
                    principalTable: "VariantAssignments",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_VariantBuildJobs_VariantTemplates_TemplateId",
                    column: x => x.TemplateId,
                    principalTable: "VariantTemplates",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "VariantDatasetItems",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ChallengeId = table.Column<int>(type: "integer", nullable: false),
                FlagContextId = table.Column<int>(type: "integer", nullable: false),
                TemplateId = table.Column<long>(type: "bigint", nullable: true),
                DatasetKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                ArtifactHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                SelectedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                SelectedParticipationId = table.Column<int>(type: "integer", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_VariantDatasetItems", x => x.Id);
                table.ForeignKey(
                    name: "FK_VariantDatasetItems_FlagContexts_FlagContextId",
                    column: x => x.FlagContextId,
                    principalTable: "FlagContexts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_VariantDatasetItems_GameChallenges_ChallengeId",
                    column: x => x.ChallengeId,
                    principalTable: "GameChallenges",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_VariantDatasetItems_Participations_SelectedParticipationId",
                    column: x => x.SelectedParticipationId,
                    principalTable: "Participations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_VariantDatasetItems_VariantTemplates_TemplateId",
                    column: x => x.TemplateId,
                    principalTable: "VariantTemplates",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(name: "IX_VariantTemplates_ChallengeId_Version",
            table: "VariantTemplates", columns: new[] { "ChallengeId", "Version" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_VariantTemplates_ManifestHash",
            table: "VariantTemplates", column: "ManifestHash");
        migrationBuilder.CreateIndex(name: "IX_VariantBuildJobs_ParticipationId",
            table: "VariantBuildJobs", column: "ParticipationId");
        migrationBuilder.CreateIndex(name: "IX_VariantBuildJobs_Status_CreatedAtUtc",
            table: "VariantBuildJobs", columns: new[] { "Status", "CreatedAtUtc" });
        migrationBuilder.CreateIndex(name: "IX_VariantBuildJobs_TemplateId_ParticipationId",
            table: "VariantBuildJobs", columns: new[] { "TemplateId", "ParticipationId" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_VariantBuildJobs_VariantAssignmentId",
            table: "VariantBuildJobs", column: "VariantAssignmentId", unique: true,
            filter: "\"VariantAssignmentId\" IS NOT NULL");
        migrationBuilder.CreateIndex(name: "IX_VariantDatasetItems_FlagContextId",
            table: "VariantDatasetItems", column: "FlagContextId", unique: true);
        migrationBuilder.CreateIndex(name: "IX_VariantDatasetItems_TemplateId",
            table: "VariantDatasetItems", column: "TemplateId");
        migrationBuilder.CreateIndex(name: "IX_VariantDatasetItems_ChallengeId_DatasetKey",
            table: "VariantDatasetItems", columns: new[] { "ChallengeId", "DatasetKey" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_VariantDatasetItems_ChallengeId_IsEnabled_SelectedAtUtc",
            table: "VariantDatasetItems", columns: new[] { "ChallengeId", "IsEnabled", "SelectedAtUtc" });
        migrationBuilder.CreateIndex(name: "IX_VariantDatasetItems_ChallengeId_SelectedParticipationId",
            table: "VariantDatasetItems", columns: new[] { "ChallengeId", "SelectedParticipationId" }, unique: true);

        migrationBuilder.Sql(
            """
            INSERT INTO "VariantDatasetItems"
                ("ChallengeId", "FlagContextId", "DatasetKey", "ArtifactHash", "IsEnabled",
                 "CreatedAtUtc", "SelectedAtUtc", "SelectedParticipationId")
            SELECT f."ChallengeId", f."Id", 'legacy-' || f."Id"::text, lf."Hash", NOT f."IsOccupied",
                   NOW(), CASE WHEN f."IsOccupied" THEN NOW() ELSE NULL END,
                   (SELECT gi."ParticipationId" FROM "GameInstances" gi
                    WHERE gi."FlagId" = f."Id" LIMIT 1)
            FROM "FlagContexts" f
            INNER JOIN "GameChallenges" c ON c."Id" = f."ChallengeId" AND c."Type" = 2
            LEFT JOIN "Attachments" a ON a."Id" = f."AttachmentId"
            LEFT JOIN "Files" lf ON lf."Id" = a."LocalFileId";
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "VariantBuildJobs");
        migrationBuilder.DropTable(name: "VariantDatasetItems");
        migrationBuilder.DropTable(name: "VariantTemplates");
    }
}
