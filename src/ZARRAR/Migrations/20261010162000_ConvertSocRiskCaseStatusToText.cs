using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZARRAR.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261010162000_ConvertSocRiskCaseStatusToText")]
public partial class ConvertSocRiskCaseStatusToText : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Older preview builds created this column as a smallint despite the model
        // and original production migration defining it as text. Make the repair
        // conditional so new installations, which already have varchar storage,
        // remain unaffected.
        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = 'SocRiskCases'
                      AND column_name = 'Status'
                      AND udt_name = 'int2'
                ) THEN
                    ALTER TABLE "SocRiskCases"
                    ALTER COLUMN "Status" TYPE character varying(20)
                    USING CASE "Status"
                        WHEN 0 THEN 'Open'
                        WHEN 1 THEN 'Investigating'
                        WHEN 2 THEN 'Resolved'
                        WHEN 3 THEN 'Clean'
                        WHEN 4 THEN 'Escalated'
                        ELSE 'Open'
                    END;
                END IF;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The previous smallint schema is invalid for the running application, so
        // intentionally leave the corrected storage in place on a rollback.
    }
}
