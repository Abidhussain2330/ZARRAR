using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BURAQ.Migrations
{
    /// <inheritdoc />
    public partial class HardenPersonalizedChallengeUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Do not silently discard anti-cheat/provenance data to make the unique indexes fit.
            // Older builds allowed these states, so fail fast with an actionable message before
            // altering indexes. Operators can then investigate and deliberately reconcile legacy data.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM "GameInstances"
                        WHERE "FlagId" IS NOT NULL
                        GROUP BY "FlagId"
                        HAVING COUNT(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'BURAQ migration blocked: duplicate GameInstances.FlagId assignments exist. Investigate personalized challenge ownership before retrying.';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM "FlagContexts"
                        WHERE "ChallengeId" IS NOT NULL
                        GROUP BY "ChallengeId", "Flag"
                        HAVING COUNT(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'BURAQ migration blocked: duplicate FlagContexts (ChallengeId, Flag) values exist. Reconcile duplicate accepted flags before retrying.';
                    END IF;
                END
                $$;
                """);

            migrationBuilder.DropIndex(
                name: "IX_GameInstances_FlagId",
                table: "GameInstances");

            migrationBuilder.DropIndex(
                name: "IX_FlagContexts_ChallengeId",
                table: "FlagContexts");

            migrationBuilder.CreateIndex(
                name: "IX_GameInstances_FlagId",
                table: "GameInstances",
                column: "FlagId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FlagContexts_ChallengeId_Flag",
                table: "FlagContexts",
                columns: new[] { "ChallengeId", "Flag" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GameInstances_FlagId",
                table: "GameInstances");

            migrationBuilder.DropIndex(
                name: "IX_FlagContexts_ChallengeId_Flag",
                table: "FlagContexts");

            migrationBuilder.CreateIndex(
                name: "IX_GameInstances_FlagId",
                table: "GameInstances",
                column: "FlagId");

            migrationBuilder.CreateIndex(
                name: "IX_FlagContexts_ChallengeId",
                table: "FlagContexts",
                column: "ChallengeId");
        }
    }
}
