using ZARRAR.Controllers;
using ZARRAR.Models.Internal;
using ZARRAR.Services.SharedGateway;
using Microsoft.Extensions.Options;
using Xunit;

namespace ZARRAR.Test;

public sealed class SharedGatewayTests
{
    private static SharedGatewayOptions ValidOptions => new()
    {
        SigningKey = "a-dedicated-test-signing-key-with-more-than-32-bytes",
        AllowedOrigins = ["https://shared.example.test"],
        TokenSeconds = 60
    };

    [Fact]
    public void TokenRoundTripPreservesServerDerivedIdentity()
    {
        var service = new SharedGatewayTokenService(Options.Create(ValidOptions));
        var now = new DateTimeOffset(2026, 9, 13, 3, 0, 0, TimeSpan.Zero);

        var token = service.Mint("challenge-7", 2, 7, 11, 19, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), now);

        Assert.True(service.TryValidate(token, "challenge-7", now.AddSeconds(30), out var claims));
        Assert.Equal(2, claims!.GameId);
        Assert.Equal(7, claims.ChallengeId);
        Assert.Equal(11, claims.ParticipationId);
        Assert.Equal(19, claims.TeamId);
    }

    [Fact]
    public void TokenRejectsTamperingAudienceAndExpiry()
    {
        var service = new SharedGatewayTokenService(Options.Create(ValidOptions));
        var now = new DateTimeOffset(2026, 9, 13, 3, 0, 0, TimeSpan.Zero);
        var token = service.Mint("challenge-7", 2, 7, 11, 19, Guid.NewGuid(), now);

        Assert.False(service.TryValidate(token + "x", "challenge-7", now, out _));
        Assert.False(service.TryValidate(token, "challenge-8", now, out _));
        Assert.False(service.TryValidate(token, "challenge-7", now.AddMinutes(2), out _));
    }

    [Theory]
    [InlineData("http://shared.example.test", false)]
    [InlineData("https://shared.example.test/path", false)]
    [InlineData("https://unlisted.example.test", false)]
    [InlineData("https://shared.example.test", true)]
    public void OriginPolicyRequiresExactAllowlistedAuthority(string value, bool expected)
    {
        var policy = new SharedGatewayOriginPolicy(Options.Create(ValidOptions));

        Assert.Equal(expected, policy.TryValidate(value, out _, out _));
    }

    [Theory]
    [InlineData("assets/app.js", true)]
    [InlineData("", true)]
    [InlineData("../admin", false)]
    [InlineData("api\\admin", false)]
    public void GatewayPathRejectsTraversal(string path, bool expected)
    {
        Assert.Equal(expected, SharedGatewayController.TryBuildRelativeTarget(path, out _));
    }

    [Theory]
    [InlineData("/healthz", true)]
    [InlineData("/nested/health", true)]
    [InlineData("//evil.example/health", false)]
    [InlineData("/../admin", false)]
    [InlineData("/%2e%2e/admin", false)]
    [InlineData("/health?target=evil", false)]
    [InlineData("/health\\admin", false)]
    public void HealthPathCannotEscapeConfiguredAuthority(string path, bool expected)
    {
        var origin = new Uri("https://shared.example.test/");

        Assert.Equal(expected, SharedRuntimeHealthUri.TryCreate(origin, path, out var target, out _));
        if (expected)
            Assert.Equal(origin.Authority, target!.Authority);
    }
}
