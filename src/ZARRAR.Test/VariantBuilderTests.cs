using ZARRAR.Models.Internal;
using ZARRAR.Models.Data;
using ZARRAR.Services.Variants;
using Microsoft.Extensions.Options;
using Xunit;

namespace ZARRAR.Test;

public sealed class VariantBuilderTests
{
    private static DeterministicSeedService Seeds(string key = "variant-seed-key-that-is-longer-than-thirty-two-bytes") =>
        new(Options.Create(new VariantBuilderOptions { SeedKey = key }));

    [Fact]
    public void SeedIsStableForSameAssignmentAndSeparatedAcrossParticipations()
    {
        var seeds = Seeds();
        var first = seeds.Derive(17, "2026.09", 4, 11);
        var repeated = seeds.Derive(17, "2026.09", 4, 11);
        var otherTeam = seeds.Derive(17, "2026.09", 4, 12);

        Assert.Equal(first, repeated);
        Assert.NotEqual(Convert.ToHexString(first), Convert.ToHexString(otherTeam));
        Assert.Equal(64, seeds.Commitment(first).Length);
    }

    [Fact]
    public void DatasetRankingIsStableAndParticipationSpecific()
    {
        var seeds = Seeds();
        Assert.Equal(seeds.Rank(4, 11, "dataset-a"), seeds.Rank(4, 11, "dataset-a"));
        Assert.NotEqual(seeds.Rank(4, 11, "dataset-a"), seeds.Rank(4, 12, "dataset-a"));
    }

    [Fact]
    public void PersonalizedPoolFallsBackToStableOrderWhenBuilderIsDisabled()
    {
        var candidates = new[]
        {
            new VariantDatasetItem { Id = 12, DatasetKey = "later" },
            new VariantDatasetItem { Id = 7, DatasetKey = "first" }
        };

        var selected = VariantDatasetSelector.SelectCandidate(candidates, Seeds(""), 4, 11);

        Assert.NotNull(selected);
        Assert.Equal(7, selected.Id);
    }

    [Fact]
    public void SeedDerivationFailsClosedWithoutAProductionStrengthKey()
    {
        var seeds = Seeds("short");
        Assert.False(seeds.IsConfigured);
        Assert.Throws<InvalidOperationException>(() => seeds.Derive(1, "v1", 1, 1));
    }

    [Fact]
    public void ManifestRequiresPinnedBuilderAndNetworkIsolation()
    {
        var validator = new VariantManifestValidator();
        var digest = $"registry.example/zarrar-builder@sha256:{new string('a', 64)}";
        var valid = validator.Validate("""{"schemaVersion":1,"entrypoint":"/builder/run","network":false}""",
            digest, "challenge.bin");
        var floatingImage = validator.Validate("""{"schemaVersion":1,"entrypoint":"/builder/run"}""",
            "registry.example/zarrar-builder:latest", "challenge.bin");
        var networked = validator.Validate("""{"schemaVersion":1,"entrypoint":"/builder/run","network":true}""",
            digest, "challenge.bin");

        Assert.True(valid.IsValid);
        Assert.Equal(64, valid.ManifestHash.Length);
        Assert.False(floatingImage.IsValid);
        Assert.Contains(floatingImage.Errors, x => x.Contains("pinned", StringComparison.OrdinalIgnoreCase));
        Assert.False(networked.IsValid);
        Assert.Contains(networked.Errors, x => x.Contains("network", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("../flag.bin")]
    [InlineData("/tmp/flag.bin")]
    public void ManifestRejectsUnsafeOutputPath(string output)
    {
        var validator = new VariantManifestValidator();
        var digest = $"registry.example/zarrar-builder@sha256:{new string('b', 64)}";
        var result = validator.Validate("""{"schemaVersion":1,"entrypoint":"/builder/run"}""",
            digest, output);
        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("valid-builder-token-that-is-more-than-32-characters", "https://builder.example.test/", 2, 67108864, true)]
    [InlineData("token with whitespace that is definitely long enough", "https://builder.example.test/", 2, 67108864, false)]
    [InlineData("REQUIRED_SCOPED_VARIANT_BUILDER_TOKEN", "https://builder.example.test/", 2, 67108864, false)]
    [InlineData("valid-builder-token-that-is-more-than-32-characters", "http://builder.example.test/", 2, 67108864, false)]
    [InlineData("valid-builder-token-that-is-more-than-32-characters", "https://builder.example.test/path", 2, 67108864, false)]
    [InlineData("valid-builder-token-that-is-more-than-32-characters", "https://builder.example.test/", 17, 67108864, false)]
    [InlineData("valid-builder-token-that-is-more-than-32-characters", "https://builder.example.test/", 2, 0, false)]
    public void ControlPlaneConfigurationFailsClosed(string token, string origin, int parallelism,
        int maxArtifactBytes, bool expected)
    {
        var options = new VariantBuilderOptions
        {
            ApiToken = token,
            ControlPlaneOrigin = origin,
            Parallelism = parallelism,
            MaxArtifactBytes = maxArtifactBytes
        };

        Assert.Equal(expected, VariantBuilderControlPlane.TryValidate(options, out _));
    }
}
