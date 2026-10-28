using BURAQ.Models.Data;
using BURAQ.Services.Soc;
using Xunit;

namespace BURAQ.Test;

/// <summary>S1/S3 checks: risk decay and the sealed SOC ledger.</summary>
public sealed class SocHardeningTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RiskDecaysWithAgeButProofDoesNot()
    {
        // Default half-life is 6 h: a 6-hour-old weight-40 signal counts as 20.
        var weak = SocRiskScoring.CalculateAt([(40, 60, "SharedWrongAnswer", Now.AddHours(-6))], Now);
        Assert.Equal(20, weak.Risk);
        Assert.Equal(60, weak.Confidence);

        // A foreign dynamic flag (confidence >= 95) stays at full weight however old it is.
        var proof = SocRiskScoring.CalculateAt([(70, 98, "ForeignFlag", Now.AddDays(-3))], Now);
        Assert.Equal(70, proof.Risk);
    }

    [Fact]
    public void FreshEvidenceMatchesTheUndecayedScore()
    {
        var rows = new (int, int, string, DateTimeOffset)[]
        {
            (5, 25, "SharedNetwork", Now),
            (15, 60, "SharedWrongAnswer", Now),
            (20, 70, "SynchronizedSolves", Now)
        };

        Assert.Equal((40, 80, 3), SocRiskScoring.CalculateAt(rows, Now));
    }

    [Fact]
    public void LedgerChainDetectsAnyEdit()
    {
        var key = new byte[32];
        var a = new SocEvent { Id = 1, EventType = "LoginFailed", Details = "x", TimestampUtc = Now };
        var b = new SocEvent { Id = 2, EventType = "FlagSubmitted", Details = "y", TimestampUtc = Now, TeamId = 4 };

        var head = SocLedger.Next(key, SocLedger.Next(key, SocLedger.GenesisHead, a), b);

        // Same input, same head.
        Assert.Equal(head, SocLedger.Next(key, SocLedger.Next(key, SocLedger.GenesisHead, a), b));

        // Changing one field of a sealed row changes the head.
        b.TeamId = 5;
        Assert.NotEqual(head, SocLedger.Next(key, SocLedger.Next(key, SocLedger.GenesisHead, a), b));

        // A different key cannot reproduce the head.
        b.TeamId = 4;
        var otherKey = Enumerable.Repeat((byte)1, 32).ToArray();
        Assert.NotEqual(head, SocLedger.Next(otherKey, SocLedger.Next(otherKey, SocLedger.GenesisHead, a), b));
    }

    [Fact]
    public void SealPayloadRoundTrips()
    {
        var seal = new SocEvent
        {
            EventType = SocLedger.SealEventType,
            PayloadJson = """{"fromId":1,"toId":9,"count":9,"previousHead":"genesis","head":"abc"}"""
        };

        Assert.Equal(new SocLedger.SealPayload(1, 9, 9, "genesis", "abc"), SocLedger.ReadSeal(seal));
        Assert.Null(SocLedger.ReadSeal(new SocEvent { EventType = "Other", PayloadJson = "{}" }));
    }
}
