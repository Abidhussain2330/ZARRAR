using BURAQ.Models;
using BURAQ.Models.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BURAQ.Test;

public sealed class TeamDeletionRelationshipTests
{
    [Fact]
    public void Deleting_a_team_removes_its_primary_soc_case_without_erasing_another_teams_case()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=tracking-only;Username=tracking;Password=tracking")
            .Options;

        using var context = new AppDbContext(options);
        var riskCase = context.Model.FindEntityType(typeof(SocRiskCase))!;

        var primaryTeam = riskCase.GetForeignKeys().Single(foreignKey =>
            foreignKey.Properties.Single().Name == nameof(SocRiskCase.PrimaryTeamId));
        var relatedTeam = riskCase.GetForeignKeys().Single(foreignKey =>
            foreignKey.Properties.Single().Name == nameof(SocRiskCase.RelatedTeamId));

        Assert.Equal(DeleteBehavior.Cascade, primaryTeam.DeleteBehavior);
        Assert.Equal(DeleteBehavior.SetNull, relatedTeam.DeleteBehavior);
    }
}
