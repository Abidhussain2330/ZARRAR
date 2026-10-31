using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ZARRAR.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Add the per-user interface preference table, and an index supporting the dashboard's
    /// time-ordered reads over accepted submissions.
    /// </summary>
    /// <remarks>
    /// Both operations are additive. No existing column is altered, no existing row is rewritten
    /// and no existing behaviour changes: a participant who has never opened the new dashboard
    /// simply has no row in "UserPreferences", and the application falls back to the documented
    /// defaults for them.
    ///
    /// The "Submissions" index is created on what is normally the largest table on the platform.
    /// PostgreSQL builds it with an ACCESS EXCLUSIVE lock, so writes to "Submissions" — that is,
    /// flag submissions — are blocked for the duration. Apply this migration outside a running
    /// competition, or build the index manually with CREATE INDEX CONCURRENTLY beforehand, in
    /// which case the CreateIndex below becomes a no-op only if the index name matches exactly.
    /// </remarks>
    public partial class AddUserPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) One row per user, holding the interface settings that used to live in browser
            //    local storage and therefore did not follow the participant between devices.
            migrationBuilder.CreateTable(
                name: "UserPreferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false, comment: "The unique identifier for the preference row.")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false, comment: "The ID of the user these preferences belong to."),
                    Motion = table.Column<byte>(type: "smallint", nullable: false, comment: "How much motion the interface should use: 0 = system, 1 = full, 2 = reduced."),
                    LowEnergyMode = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether the low-energy presentation is enabled."),
                    SidebarCollapsed = table.Column<bool>(type: "boolean", nullable: false, comment: "Whether the navigation sidebar is collapsed."),
                    UpdateTimeUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "The time the preferences were last written.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPreferences", x => x.Id);

                    // Preferences are worthless once their owner is gone, and the row carries no
                    // history anybody would want to keep, so it is removed with the account
                    // rather than orphaned.
                    table.ForeignKey(
                        name: "FK_UserPreferences_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Stores per-user interface preferences for the participant dashboard.");

            // 2) Enforces "one row per user" in the database rather than only in application code,
            //    so a lost race on first write fails loudly instead of silently duplicating.
            migrationBuilder.CreateIndex(
                name: "IX_UserPreferences_UserId",
                table: "UserPreferences",
                column: "UserId",
                unique: true);

            // 3) Supports the dashboard's two time-ordered reads over accepted submissions: the
            //    newest N accepted submissions, and the count of accepted submissions in the last
            //    24 hours. Every pre-existing index on "Submissions" leads with an entity id, so
            //    without this both queries scan the whole table and the first also sorts it.
            migrationBuilder.CreateIndex(
                name: "IX_Submissions_Status_SubmitTimeUtc",
                table: "Submissions",
                columns: ["Status", "SubmitTimeUtc"]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 1) Drop the index first: it is an optimisation only, and nothing else depends on it.
            migrationBuilder.DropIndex(
                name: "IX_Submissions_Status_SubmitTimeUtc",
                table: "Submissions");

            // 2) Dropping the table takes its primary key, its foreign key and its unique index
            //    with it. Stored preferences are lost, which is the intended result of rolling
            //    back the feature that introduced them; no other table references this one.
            migrationBuilder.DropTable(
                name: "UserPreferences");
        }
    }
}
