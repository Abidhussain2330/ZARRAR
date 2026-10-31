using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZARRAR.Migrations
{
    /// <inheritdoc />
    public partial class AddApiTokenScopeAndExpiry : Migration
    {
        /// <summary>
        /// TokenScope.All (int.MaxValue).
        /// </summary>
        /// <remarks>
        /// Tokens created before scoping existed were unscoped and were accepted by the privilege
        /// filter ahead of the role check, i.e. they behaved as admin-equivalent credentials. They
        /// are therefore migrated with the full scope so that every existing integration keeps
        /// working exactly as it did before the upgrade.
        /// OPERATOR ACTION REQUIRED: after upgrading, review every row in "ApiTokens" that still
        /// holds "Scope" = 2147483647 and re-issue it with the narrowest scope its automation
        /// actually needs.
        /// </remarks>
        private const int FullScope = 2147483647;

        /// <summary>
        /// Explicit far-future expiry replacing the NULL ("never expires") values the old schema
        /// allowed.
        /// </summary>
        /// <remarks>
        /// The rows are preserved rather than deleted or invalidated, so no operator's automation
        /// breaks on upgrade, while the column still becomes NOT NULL and every newly issued token
        /// is bounded by Limits.MaxApiTokenLifetimeDays.
        /// OPERATOR ACTION REQUIRED: these tokens are identifiable by an "ExpiresAt" of
        /// 2099-12-31T23:59:59Z and must be rotated onto a bounded lifetime.
        /// </remarks>
        private const string FarFutureExpiry = "2099-12-31T23:59:59Z";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Add the scope column, granting existing rows the full scope (see FullScope above).
            migrationBuilder.AddColumn<int>(
                name: "Scope",
                table: "ApiTokens",
                type: "integer",
                nullable: false,
                defaultValue: FullScope,
                comment: "The capabilities the token is allowed to exercise.");

            // 2) The full scope is a one-off backfill for pre-existing rows, not a policy for new
            //    ones: every token issued from now on names its scope explicitly, so the column
            //    default is removed once the backfill has been applied.
            migrationBuilder.Sql(@"ALTER TABLE ""ApiTokens"" ALTER COLUMN ""Scope"" DROP DEFAULT;");

            // 3) Replace the NULL ("never expires") values with an explicit far-future timestamp.
            migrationBuilder.Sql($@"
                UPDATE ""ApiTokens""
                SET ""ExpiresAt"" = TIMESTAMPTZ '{FarFutureExpiry}'
                WHERE ""ExpiresAt"" IS NULL;
            ");

            // 4) Expiry is now mandatory at the schema level. This can only succeed because step 3
            //    removed every NULL.
            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ExpiresAt",
                table: "ApiTokens",
                type: "timestamp with time zone",
                nullable: false,
                comment: "The timestamp when the token expires.",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "The timestamp when the token expires. A null value means it never expires.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 1) Restore the nullable column.
            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ExpiresAt",
                table: "ApiTokens",
                type: "timestamp with time zone",
                nullable: true,
                comment: "The timestamp when the token expires. A null value means it never expires.",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "The timestamp when the token expires.");

            // 2) Restore the pre-migration "never expires" representation for exactly the rows the
            //    Up backfill rewrote, so a rollback leaves those tokens as they were found.
            migrationBuilder.Sql($@"
                UPDATE ""ApiTokens""
                SET ""ExpiresAt"" = NULL
                WHERE ""ExpiresAt"" = TIMESTAMPTZ '{FarFutureExpiry}';
            ");

            // 3) Drop the scope column. Tokens revert to the unscoped behaviour of the old schema.
            migrationBuilder.DropColumn(
                name: "Scope",
                table: "ApiTokens");
        }
    }
}
