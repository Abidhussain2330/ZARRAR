using BURAQ.Models;
using BURAQ.Models.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BURAQ.Test;

public sealed class GameDeletionRelationshipTests
{
    [Fact]
    public void Deleting_a_game_cleans_up_its_runtime_variant_and_soc_records()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=tracking-only;Username=tracking;Password=tracking")
            .Options;

        using var context = new AppDbContext(options);

        Assert.Equal(DeleteBehavior.Cascade, DeleteBehaviorFor<SocRiskCase>(context, nameof(SocRiskCase.GameId)));
        Assert.Equal(DeleteBehavior.Cascade,
            DeleteBehaviorFor<SharedRuntimeConfiguration>(context, nameof(SharedRuntimeConfiguration.ChallengeId)));
        Assert.Equal(DeleteBehavior.Cascade,
            DeleteBehaviorFor<SharedRuntimeSession>(context, nameof(SharedRuntimeSession.ParticipationId)));
        Assert.Equal(DeleteBehavior.Cascade,
            DeleteBehaviorFor<VariantAssignment>(context, nameof(VariantAssignment.ChallengeId)));
        Assert.Equal(DeleteBehavior.Cascade,
            DeleteBehaviorFor<VariantAssignment>(context, nameof(VariantAssignment.ParticipationId)));
        Assert.Equal(DeleteBehavior.Cascade,
            DeleteBehaviorFor<VariantAssignment>(context, nameof(VariantAssignment.FlagContextId)));
        Assert.Equal(DeleteBehavior.Cascade,
            DeleteBehaviorFor<VariantBuildJob>(context, nameof(VariantBuildJob.ParticipationId)));
        Assert.Equal(DeleteBehavior.Cascade,
            DeleteBehaviorFor<VariantBuildJob>(context, nameof(VariantBuildJob.TemplateId)));
        Assert.Equal(DeleteBehavior.Cascade,
            DeleteBehaviorFor<VariantBuildJob>(context, nameof(VariantBuildJob.VariantAssignmentId)));
        Assert.Equal(DeleteBehavior.Cascade,
            DeleteBehaviorFor<VariantDatasetItem>(context, nameof(VariantDatasetItem.ChallengeId)));
        Assert.Equal(DeleteBehavior.Cascade,
            DeleteBehaviorFor<VariantDatasetItem>(context, nameof(VariantDatasetItem.FlagContextId)));
        Assert.Equal(DeleteBehavior.SetNull,
            DeleteBehaviorFor<VariantDatasetItem>(context, nameof(VariantDatasetItem.SelectedParticipationId)));
        Assert.Equal(DeleteBehavior.SetNull,
            DeleteBehaviorFor<SocRiskEvidence>(context, nameof(SocRiskEvidence.SubmissionId)));
        Assert.Equal(DeleteBehavior.Cascade,
            DeleteBehaviorFor<VariantTemplate>(context, nameof(VariantTemplate.ChallengeId)));
    }

    private static DeleteBehavior DeleteBehaviorFor<TEntity>(AppDbContext context, string propertyName)
        where TEntity : class
        => context.Model.FindEntityType(typeof(TEntity))!.GetForeignKeys().Single(foreignKey =>
            foreignKey.Properties.Single().Name == propertyName).DeleteBehavior;

}
