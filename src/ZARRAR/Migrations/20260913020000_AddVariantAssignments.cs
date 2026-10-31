using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ZARRAR.Migrations;

public partial class AddVariantAssignments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "VariantAssignments",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy",
                        NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                AssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                ChallengeId = table.Column<int>(type: "integer", nullable: false),
                ParticipationId = table.Column<int>(type: "integer", nullable: false),
                FlagContextId = table.Column<int>(type: "integer", nullable: false),
                AssignedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                FirstDownloadedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                LastDownloadedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                DownloadCount = table.Column<int>(type: "integer", nullable: false),
                ArtifactHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                Mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                GeneratorVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                SeedCommitment = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                MetadataJson = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                RetiredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_VariantAssignments", x => x.Id);
                table.ForeignKey(
                    name: "FK_VariantAssignments_GameChallenges_ChallengeId",
                    column: x => x.ChallengeId,
                    principalTable: "GameChallenges",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_VariantAssignments_FlagContexts_FlagContextId",
                    column: x => x.FlagContextId,
                    principalTable: "FlagContexts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_VariantAssignments_Participations_ParticipationId",
                    column: x => x.ParticipationId,
                    principalTable: "Participations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.Sql(
            """
            INSERT INTO "VariantAssignments"
                ("AssignmentId", "ChallengeId", "ParticipationId", "FlagContextId",
                 "AssignedAtUtc", "DownloadCount", "ArtifactHash", "Mode")
            SELECT gen_random_uuid(), gi."ChallengeId", gi."ParticipationId", gi."FlagId",
                   NOW(), 0, lf."Hash", 'LegacyPool'
            FROM "GameInstances" gi
            INNER JOIN "GameChallenges" c ON c."Id" = gi."ChallengeId"
            INNER JOIN "FlagContexts" f ON f."Id" = gi."FlagId"
            LEFT JOIN "Attachments" a ON a."Id" = f."AttachmentId"
            LEFT JOIN "Files" lf ON lf."Id" = a."LocalFileId"
            WHERE gi."FlagId" IS NOT NULL
              AND c."Type" = 2;
            """);

        migrationBuilder.CreateIndex(
            name: "IX_VariantAssignments_ArtifactHash",
            table: "VariantAssignments",
            column: "ArtifactHash");

        migrationBuilder.CreateIndex(
            name: "IX_VariantAssignments_AssignmentId",
            table: "VariantAssignments",
            column: "AssignmentId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_VariantAssignments_ChallengeId_ParticipationId",
            table: "VariantAssignments",
            columns: new[] { "ChallengeId", "ParticipationId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_VariantAssignments_FlagContextId",
            table: "VariantAssignments",
            column: "FlagContextId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_VariantAssignments_ParticipationId",
            table: "VariantAssignments",
            column: "ParticipationId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "VariantAssignments");
    }
}
