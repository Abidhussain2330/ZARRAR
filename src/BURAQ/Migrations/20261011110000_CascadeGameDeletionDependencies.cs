using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BURAQ.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261011110000_CascadeGameDeletionDependencies")]
public partial class CascadeGameDeletionDependencies : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ReplaceDeleteBehavior(migrationBuilder, "SocRiskCases", "FK_SocRiskCases_Games_GameId", "GameId", "Games",
            ReferentialAction.Cascade);
        ReplaceDeleteBehavior(migrationBuilder, "SharedRuntimeConfigurations",
            "FK_SharedRuntimeConfigurations_GameChallenges_ChallengeId", "ChallengeId", "GameChallenges",
            ReferentialAction.Cascade);
        ReplaceDeleteBehavior(migrationBuilder, "SharedRuntimeSessions",
            "FK_SharedRuntimeSessions_Participations_ParticipationId", "ParticipationId", "Participations",
            ReferentialAction.Cascade);
        ReplaceDeleteBehavior(migrationBuilder, "VariantAssignments",
            "FK_VariantAssignments_GameChallenges_ChallengeId", "ChallengeId", "GameChallenges",
            ReferentialAction.Cascade);
        ReplaceDeleteBehavior(migrationBuilder, "VariantAssignments",
            "FK_VariantAssignments_FlagContexts_FlagContextId", "FlagContextId", "FlagContexts",
            ReferentialAction.Cascade);
        ReplaceDeleteBehavior(migrationBuilder, "VariantAssignments",
            "FK_VariantAssignments_Participations_ParticipationId", "ParticipationId", "Participations",
            ReferentialAction.Cascade);
        ReplaceDeleteBehavior(migrationBuilder, "VariantBuildJobs",
            "FK_VariantBuildJobs_Participations_ParticipationId", "ParticipationId", "Participations",
            ReferentialAction.Cascade);
        ReplaceDeleteBehavior(migrationBuilder, "VariantBuildJobs",
            "FK_VariantBuildJobs_VariantTemplates_TemplateId", "TemplateId", "VariantTemplates",
            ReferentialAction.Cascade);
        ReplaceDeleteBehavior(migrationBuilder, "VariantBuildJobs",
            "FK_VariantBuildJobs_VariantAssignments_VariantAssignmentId", "VariantAssignmentId", "VariantAssignments",
            ReferentialAction.Cascade);
        ReplaceDeleteBehavior(migrationBuilder, "VariantDatasetItems",
            "FK_VariantDatasetItems_GameChallenges_ChallengeId", "ChallengeId", "GameChallenges",
            ReferentialAction.Cascade);
        ReplaceDeleteBehavior(migrationBuilder, "VariantDatasetItems",
            "FK_VariantDatasetItems_FlagContexts_FlagContextId", "FlagContextId", "FlagContexts",
            ReferentialAction.Cascade);
        ReplaceDeleteBehavior(migrationBuilder, "VariantDatasetItems",
            "FK_VariantDatasetItems_Participations_SelectedParticipationId", "SelectedParticipationId", "Participations",
            ReferentialAction.SetNull);
        ReplaceDeleteBehavior(migrationBuilder, "VariantTemplates",
            "FK_VariantTemplates_GameChallenges_ChallengeId", "ChallengeId", "GameChallenges",
            ReferentialAction.Cascade);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ReplaceDeleteBehavior(migrationBuilder, "SocRiskCases", "FK_SocRiskCases_Games_GameId", "GameId", "Games",
            ReferentialAction.Restrict);
        ReplaceDeleteBehavior(migrationBuilder, "SharedRuntimeConfigurations",
            "FK_SharedRuntimeConfigurations_GameChallenges_ChallengeId", "ChallengeId", "GameChallenges",
            ReferentialAction.Restrict);
        ReplaceDeleteBehavior(migrationBuilder, "SharedRuntimeSessions",
            "FK_SharedRuntimeSessions_Participations_ParticipationId", "ParticipationId", "Participations",
            ReferentialAction.Restrict);
        ReplaceDeleteBehavior(migrationBuilder, "VariantAssignments",
            "FK_VariantAssignments_GameChallenges_ChallengeId", "ChallengeId", "GameChallenges",
            ReferentialAction.Restrict);
        ReplaceDeleteBehavior(migrationBuilder, "VariantAssignments",
            "FK_VariantAssignments_FlagContexts_FlagContextId", "FlagContextId", "FlagContexts",
            ReferentialAction.Restrict);
        ReplaceDeleteBehavior(migrationBuilder, "VariantAssignments",
            "FK_VariantAssignments_Participations_ParticipationId", "ParticipationId", "Participations",
            ReferentialAction.Restrict);
        ReplaceDeleteBehavior(migrationBuilder, "VariantBuildJobs",
            "FK_VariantBuildJobs_Participations_ParticipationId", "ParticipationId", "Participations",
            ReferentialAction.Restrict);
        ReplaceDeleteBehavior(migrationBuilder, "VariantBuildJobs",
            "FK_VariantBuildJobs_VariantTemplates_TemplateId", "TemplateId", "VariantTemplates",
            ReferentialAction.Restrict);
        ReplaceDeleteBehavior(migrationBuilder, "VariantBuildJobs",
            "FK_VariantBuildJobs_VariantAssignments_VariantAssignmentId", "VariantAssignmentId", "VariantAssignments",
            ReferentialAction.Restrict);
        ReplaceDeleteBehavior(migrationBuilder, "VariantDatasetItems",
            "FK_VariantDatasetItems_GameChallenges_ChallengeId", "ChallengeId", "GameChallenges",
            ReferentialAction.Restrict);
        ReplaceDeleteBehavior(migrationBuilder, "VariantDatasetItems",
            "FK_VariantDatasetItems_FlagContexts_FlagContextId", "FlagContextId", "FlagContexts",
            ReferentialAction.Restrict);
        ReplaceDeleteBehavior(migrationBuilder, "VariantDatasetItems",
            "FK_VariantDatasetItems_Participations_SelectedParticipationId", "SelectedParticipationId", "Participations",
            ReferentialAction.Restrict);
        ReplaceDeleteBehavior(migrationBuilder, "VariantTemplates",
            "FK_VariantTemplates_GameChallenges_ChallengeId", "ChallengeId", "GameChallenges",
            ReferentialAction.Restrict);
    }

    private static void ReplaceDeleteBehavior(MigrationBuilder migrationBuilder, string table, string name,
        string column, string principalTable, ReferentialAction onDelete)
    {
        migrationBuilder.DropForeignKey(name: name, table: table);
        migrationBuilder.AddForeignKey(name: name, table: table, column: column, principalTable: principalTable,
            principalColumn: "Id", onDelete: onDelete);
    }
}
