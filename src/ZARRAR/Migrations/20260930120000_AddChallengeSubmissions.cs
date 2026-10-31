using ZARRAR.Models;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZARRAR.Migrations;

/// <summary>
/// Challenge developer submissions.
/// </summary>
/// <remarks>
/// The original version of this migration had no [DbContext]/[Migration] attributes (so EF never
/// discovered it) and used SQLite column types. It is now PostgreSQL-native and idempotent, so it
/// is safe on databases where the table was already created by an earlier build.
/// </remarks>
[DbContext(typeof(AppDbContext))]
[Migration("20260930120000_AddChallengeSubmissions")]
public partial class AddChallengeSubmissions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE "AspNetUsers"
                ADD COLUMN IF NOT EXISTS "IsChallengeDeveloper" boolean NOT NULL DEFAULT FALSE;

            CREATE TABLE IF NOT EXISTS "ChallengeSubmissions" (
                "Id" uuid NOT NULL,
                "Title" character varying(128) NOT NULL,
                "Description" character varying(4000) NOT NULL,
                "PackageName" character varying(255) NOT NULL,
                "PackageHash" character varying(64) NOT NULL,
                "PackageSize" bigint NOT NULL,
                "SubmittedUtc" timestamp with time zone NOT NULL,
                "ReviewedUtc" timestamp with time zone NULL,
                "Status" integer NOT NULL,
                "ReviewNote" character varying(2000) NOT NULL,
                "DeveloperId" uuid NOT NULL,
                "ReviewerId" uuid NULL,
                CONSTRAINT "PK_ChallengeSubmissions" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_ChallengeSubmissions_AspNetUsers_DeveloperId" FOREIGN KEY ("DeveloperId")
                    REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS "IX_ChallengeSubmissions_DeveloperId_SubmittedUtc"
                ON "ChallengeSubmissions" ("DeveloperId", "SubmittedUtc");
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS "ChallengeSubmissions";
            ALTER TABLE "AspNetUsers" DROP COLUMN IF EXISTS "IsChallengeDeveloper";
            """);
    }
}
