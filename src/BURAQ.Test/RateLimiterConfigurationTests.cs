using System.Collections;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Threading.RateLimiting;
using BuraqRateLimiter = BURAQ.Middlewares.RateLimiter;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace BURAQ.Test;

public sealed class RateLimiterConfigurationTests
{
    [Fact]
    public void AuthenticatedGlobalPartitionHasFinitePerUserLimit()
    {
        var options = new RateLimiterOptions();
        BuraqRateLimiter.ConfigureRateLimiter(options);
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "test"))
        };

        for (var i = 0; i < 150; i++)
        {
            using var permitted = options.GlobalLimiter!.AttemptAcquire(context);
            Assert.True(permitted.IsAcquired);
        }

        using var lease = options.GlobalLimiter!.AttemptAcquire(context);

        Assert.False(lease.IsAcquired);
        Assert.Equal(StatusCodes.Status429TooManyRequests, options.RejectionStatusCode);
    }

    [Fact]
    public void LoopbackGlobalPartitionIsGenerousButFinite()
    {
        var options = new RateLimiterOptions();
        BuraqRateLimiter.ConfigureRateLimiter(options);
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;

        for (var i = 0; i < 3000; i++)
        {
            using var permitted = options.GlobalLimiter!.AttemptAcquire(context);
            Assert.True(permitted.IsAcquired);
        }

        using var lease = options.GlobalLimiter!.AttemptAcquire(context);

        Assert.False(lease.IsAcquired);
    }

    [Fact]
    public void LoginPolicyAllowsThreeHundredAttemptsPerSourceIpBeforeRejecting()
    {
        var options = new RateLimiterOptions();
        BuraqRateLimiter.ConfigureRateLimiter(options);
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");

        using var limiter = CreateNamedLimiter(options, nameof(BuraqRateLimiter.LimitPolicy.Login), context);

        for (var i = 0; i < 300; i++)
        {
            using var permitted = limiter.AttemptAcquire(1);
            Assert.True(permitted.IsAcquired);
        }

        using var rejected = limiter.AttemptAcquire(1);

        Assert.False(rejected.IsAcquired);
    }

    private static RateLimiter CreateNamedLimiter(RateLimiterOptions options, string policyName, HttpContext context)
    {
        var policyMapField = typeof(RateLimiterOptions).GetField("<PolicyMap>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(policyMapField is not null,
            $"Unable to locate named policy storage. Fields: {string.Join(", ", typeof(RateLimiterOptions).GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Select(field => field.Name))}");
        var policyMap = Assert.IsAssignableFrom<IDictionary>(policyMapField.GetValue(options));
        var policy = policyMap[policyName];
        Assert.NotNull(policy);
        var partition = policy!.GetType().GetMethod("GetPartition")!.Invoke(policy, [context])!;
        var partitionType = partition.GetType();
        var factory = partitionType.GetProperty("Factory")!.GetValue(partition)!;
        var partitionKey = partitionType.GetProperty("PartitionKey")!.GetValue(partition);

        return Assert.IsAssignableFrom<RateLimiter>(factory.GetType().GetMethod("Invoke")!.Invoke(factory, [partitionKey]));
    }
}
