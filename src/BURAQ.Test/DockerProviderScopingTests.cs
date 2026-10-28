using BURAQ.Services.Container;
using BURAQ.Services.Container.Manager;
using Docker.DotNet.Models;
using Xunit;
using BuraqContainerConfig = BURAQ.Models.Internal.ContainerConfig;

namespace BURAQ.Test;

public sealed class DockerProviderScopingTests
{
    [Fact]
    public void GenericChallengeLabelsDoNotClaimBuraqOwnership()
    {
        var unrelated = new ContainerListResponse
        {
            Labels = new Dictionary<string, string>
            {
                ["ChallengeId"] = "17",
                ["TeamId"] = "46",
                ["UserId"] = Guid.NewGuid().ToString()
            }
        };

        Assert.False(DockerManager.IsChallengeWorkload(unrelated));
    }

    [Fact]
    public void PartialNamespacedLabelsDoNotClaimBuraqOwnership()
    {
        var partial = new ContainerListResponse
        {
            Labels = new Dictionary<string, string>
            {
                [ContainerLabels.Key(ContainerLabels.ChallengeId)] = "7"
            }
        };

        Assert.False(DockerManager.IsChallengeWorkload(partial));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompleteCurrentOrLegacyOwnershipLabelsAreDiscovered(bool legacy)
    {
        var key = legacy
            ? (Func<string, string>)ContainerLabels.LegacyKey
            : ContainerLabels.Key;
        var workload = new ContainerListResponse
        {
            Labels = new Dictionary<string, string>
            {
                [key(ContainerLabels.ResourceId)] = "local-resource",
                [key(ContainerLabels.ChallengeId)] = "7"
            }
        };

        Assert.True(DockerManager.IsChallengeWorkload(workload));
    }

    [Fact]
    public void NewDockerWorkloadsCarryOwnedAndCompatibilityLabels()
    {
        var userId = Guid.NewGuid();
        var config = new BuraqContainerConfig
        {
            Image = "local/buraq-safe:cp07",
            TeamId = "2",
            UserId = userId,
            ChallengeId = 7,
            GameId = 1
        };

        var labels = DockerManager.BuildChallengeLabels("resource-123", config);

        Assert.Equal("resource-123", labels[ContainerLabels.Key(ContainerLabels.ResourceId)]);
        Assert.Equal("7", labels[ContainerLabels.Key(ContainerLabels.ChallengeId)]);
        Assert.Equal("resource-123", labels[ContainerLabels.LegacyKey(ContainerLabels.ResourceId)]);
        Assert.Equal("7", labels[ContainerLabels.LegacyKey(ContainerLabels.ChallengeId)]);
        Assert.Equal("7", labels["ChallengeId"]);
        Assert.Equal("2", labels["TeamId"]);
        Assert.Equal(userId.ToString(), labels["UserId"]);
    }
}
