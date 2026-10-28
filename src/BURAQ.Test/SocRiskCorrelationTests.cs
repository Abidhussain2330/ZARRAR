using BURAQ.Services.Soc;
using Xunit;

namespace BURAQ.Test;

public sealed class SocRiskCorrelationTests
{
    [Fact]
    public void PairKeyIsSymmetricAndGameScoped()
    {
        Assert.Equal(SocRiskCaseService.PairKey(7, 11, 4), SocRiskCaseService.PairKey(7, 4, 11));
        Assert.NotEqual(SocRiskCaseService.PairKey(7, 4, 11), SocRiskCaseService.PairKey(8, 4, 11));
    }

    [Fact]
    public void RiskScoreAccumulatesEvidenceButIsCapped()
    {
        var score = SocRiskScoring.Calculate([
            (70, 98, "ForeignFlag"),
            (15, 99, "HeadToHeadTransfer"),
            (30, 100, "ReciprocalFlagExchange")
        ]);

        Assert.Equal(100, score.Risk);
        Assert.Equal(100, score.Confidence);
        Assert.Equal(3, score.RuleHits);
    }

    [Fact]
    public void ConfidenceRewardsIndependentRulesWithoutExceedingOneHundred()
    {
        var independent = SocRiskScoring.Calculate([
            (5, 25, "SharedNetwork"),
            (15, 60, "SharedWrongAnswer"),
            (20, 70, "SynchronizedSolves")
        ]);
        var duplicate = SocRiskScoring.Calculate([
            (5, 25, "SharedNetwork"),
            (5, 25, "SharedNetwork")
        ]);

        Assert.Equal((40, 80, 3), independent);
        Assert.Equal((10, 25, 1), duplicate);
    }

    [Fact]
    public void InvalidWeightsAndConfidenceAreClamped()
    {
        var score = SocRiskScoring.Calculate([(-25, -5, "A"), (250, 500, "B")]);

        Assert.Equal((100, 100, 2), score);
    }
}
