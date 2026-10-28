using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BURAQ.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Change the column default for challenge network mode from Open (0) to Isolated (32).
    /// </summary>
    /// <remarks>
    /// Challenge containers defaulted to an open network, so a challenge could reach the platform,
    /// its database, its cache and the cloud instance metadata service (finding F-020).
    ///
    /// This alters the column DEFAULT only. Every existing row already holds a concrete value —
    /// the AddNetworkMode migration backfilled them with 0 when the column was created — so no
    /// existing challenge changes behaviour on upgrade and no running competition is disrupted.
    /// Only challenges created after this migration are default-deny.
    ///
    /// OPERATOR ACTION RECOMMENDED: review existing challenges still set to Open and move any that
    /// do not genuinely need outbound connectivity to Isolated. Challenges that stay Open are
    /// still protected by the widened egress deny-list shipped in the same release.
    /// </remarks>
    public partial class DefaultChallengeNetworkIsolated : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<byte>(
                name: "NetworkMode",
                table: "GameChallenges",
                type: "smallint",
                nullable: true,
                defaultValue: (byte)32,
                oldClrType: typeof(byte),
                oldType: "smallint",
                oldNullable: true,
                oldDefaultValue: (byte)0);

            migrationBuilder.AlterColumn<byte>(
                name: "NetworkMode",
                table: "ExerciseChallenges",
                type: "smallint",
                nullable: true,
                defaultValue: (byte)32,
                oldClrType: typeof(byte),
                oldType: "smallint",
                oldNullable: true,
                oldDefaultValue: (byte)0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<byte>(
                name: "NetworkMode",
                table: "GameChallenges",
                type: "smallint",
                nullable: true,
                defaultValue: (byte)0,
                oldClrType: typeof(byte),
                oldType: "smallint",
                oldNullable: true,
                oldDefaultValue: (byte)32);

            migrationBuilder.AlterColumn<byte>(
                name: "NetworkMode",
                table: "ExerciseChallenges",
                type: "smallint",
                nullable: true,
                defaultValue: (byte)0,
                oldClrType: typeof(byte),
                oldType: "smallint",
                oldNullable: true,
                oldDefaultValue: (byte)32);
        }
    }
}
