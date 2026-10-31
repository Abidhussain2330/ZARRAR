using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ZARRAR.Migrations;

public partial class AddSharedRuntimeGateway : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SharedRuntimeConfigurations",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                ChallengeId = table.Column<int>(type: "integer", nullable: false),
                Origin = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                Audience = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                HealthPath = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                StateMode = table.Column<byte>(type: "smallint", nullable: false),
                IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                MaxConcurrentSessions = table.Column<int>(type: "integer", nullable: false),
                HealthStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                LastHealthError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                LastHealthCheckUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SharedRuntimeConfigurations", x => x.Id);
                table.ForeignKey(
                    name: "FK_SharedRuntimeConfigurations_GameChallenges_ChallengeId",
                    column: x => x.ChallengeId,
                    principalTable: "GameChallenges",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "SharedRuntimeSessions",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                RuntimeConfigurationId = table.Column<long>(type: "bigint", nullable: false),
                ParticipationId = table.Column<int>(type: "integer", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                LastAccessAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SharedRuntimeSessions", x => x.Id);
                table.ForeignKey(
                    name: "FK_SharedRuntimeSessions_Participations_ParticipationId",
                    column: x => x.ParticipationId,
                    principalTable: "Participations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_SharedRuntimeSessions_RuntimeConfiguration",
                    column: x => x.RuntimeConfigurationId,
                    principalTable: "SharedRuntimeConfigurations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_SharedRuntimeConfigurations_ChallengeId",
            table: "SharedRuntimeConfigurations",
            column: "ChallengeId",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_SharedRuntimeSessions_ExpiresAtUtc",
            table: "SharedRuntimeSessions",
            column: "ExpiresAtUtc");
        migrationBuilder.CreateIndex(
            name: "IX_SharedRuntimeSessions_ParticipationId",
            table: "SharedRuntimeSessions",
            column: "ParticipationId");
        migrationBuilder.CreateIndex(
            name: "IX_SharedRuntimeSessions_RuntimeConfigurationId_ParticipationId",
            table: "SharedRuntimeSessions",
            columns: new[] { "RuntimeConfigurationId", "ParticipationId" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_SharedRuntimeSessions_SessionId",
            table: "SharedRuntimeSessions",
            column: "SessionId",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "SharedRuntimeSessions");
        migrationBuilder.DropTable(name: "SharedRuntimeConfigurations");
    }
}
