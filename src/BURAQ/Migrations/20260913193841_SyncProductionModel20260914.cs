using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BURAQ.Migrations
{
    /// <inheritdoc />
    public partial class SyncProductionModel20260914 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // AddVariantDatasetSelectedParticipationIndex already creates this index, so a
            // plain CreateIndex fails on any database that ran that migration.
            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS "IX_VariantDatasetItems_SelectedParticipationId"
                ON "VariantDatasetItems" ("SelectedParticipationId");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "IX_VariantDatasetItems_SelectedParticipationId";
                """);
        }
    }
}
