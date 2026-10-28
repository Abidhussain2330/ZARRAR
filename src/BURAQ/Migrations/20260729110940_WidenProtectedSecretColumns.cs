using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BURAQ.Migrations
{
    /// <summary>
    /// Widens <c>Games.PrivateKey</c> so an AES-256-GCM protected signing key fits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The F-017 remediation replaced the legacy raw XOR (32 plaintext bytes, 44 Base64
    /// characters) with a <c>SecretProtector</c> envelope of magic (4) + version (1) + nonce (12)
    /// + ciphertext (32) + GCM tag (16) = 65 bytes, i.e. 88 Base64 characters, but the column
    /// stayed at <c>character varying(63)</c>. Every game insert therefore failed with
    /// <c>22001: value too long</c> on any deployment with <c>XorKey</c> configured. Only the
    /// private key is affected; the public key is stored unsealed at 44 characters.
    /// </para>
    /// <para>
    /// Widening a <c>varchar</c> in PostgreSQL is a catalogue-only change: no table rewrite, no
    /// data movement, existing rows are untouched. <c>Down</c> is a genuine narrowing and will be
    /// <em>rejected</em> by PostgreSQL if any stored key is longer than 63 characters, which is
    /// every key written since F-017. That is deliberate — it fails closed rather than truncating
    /// signing keys — so <c>Down</c> is only applicable to a database that has not yet stored a
    /// protected key.
    /// </para>
    /// </remarks>
    public partial class WidenProtectedSecretColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "PrivateKey",
                table: "Games",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(63)",
                oldMaxLength: 63);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "PrivateKey",
                table: "Games",
                type: "character varying(63)",
                maxLength: 63,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);
        }
    }
}
