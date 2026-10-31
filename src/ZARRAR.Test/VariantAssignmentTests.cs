using ZARRAR.Models.Data;
using ZARRAR.Utils;
using Xunit;

namespace ZARRAR.Test;

public sealed class VariantAssignmentTests
{
    [Fact]
    public void RegisterDownloadTracksFirstLastAndCount()
    {
        var assignedAt = new DateTimeOffset(2026, 9, 13, 1, 0, 0, TimeSpan.Zero);
        var assignment = new VariantAssignment { AssignedAtUtc = assignedAt };

        assignment.RegisterDownload(assignedAt.AddMinutes(2));
        assignment.RegisterDownload(assignedAt.AddMinutes(5));

        Assert.Equal(2, assignment.DownloadCount);
        Assert.Equal(assignedAt.AddMinutes(2), assignment.FirstDownloadedAtUtc);
        Assert.Equal(assignedAt.AddMinutes(5), assignment.LastDownloadedAtUtc);
    }

    [Fact]
    public void RegisterDownloadRejectsTimestampsBeforeAssignment()
    {
        var assignedAt = new DateTimeOffset(2026, 9, 13, 1, 0, 0, TimeSpan.Zero);
        var assignment = new VariantAssignment { AssignedAtUtc = assignedAt };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            assignment.RegisterDownload(assignedAt.AddTicks(-1)));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    public void ArtifactRouteRejectsInvalidIdentifiers(int gameId, int challengeId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PersonalizedArtifactRoutes.ForGame(gameId, challengeId));
    }

    [Fact]
    public void ArtifactRouteUsesAuthenticatedGameApi()
    {
        Assert.Equal(
            "/api/game/42/challenges/17/artifact",
            PersonalizedArtifactRoutes.ForGame(42, 17));
    }

    [Fact]
    public void StaticAttachmentRouteUsesAuthenticatedGameApi()
    {
        Assert.Equal(
            "/api/game/42/challenges/17/attachment",
            ChallengeAttachmentRoutes.ForGame(42, 17));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void GenericAssetRestrictionProtectsChallengeBlobs(bool challengeAsset, bool administrator,
        bool expected)
    {
        Assert.Equal(expected, ChallengeAssetAccess.IsRestricted(challengeAsset, administrator));
    }

    [Theory]
    [InlineData(true, "private, no-store, no-cache, max-age=0")]
    [InlineData(false, "public, max-age=604800")]
    public void GenericAssetCachePolicyNeverPubliclyCachesChallengeBlobs(bool challengeAsset, string expected)
    {
        Assert.Equal(expected, ChallengeAssetAccess.CacheControl(challengeAsset));
    }
}
