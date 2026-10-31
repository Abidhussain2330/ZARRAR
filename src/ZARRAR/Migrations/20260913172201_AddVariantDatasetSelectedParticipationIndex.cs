using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZARRAR.Migrations
{
    /// <inheritdoc />
    public partial class AddVariantDatasetSelectedParticipationIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
