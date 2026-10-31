using Xunit;

namespace ZARRAR.Test;

public sealed class MiddlewareSecurityOrderTests
{
    [Fact]
    public void AuthenticationRunsBeforeRateLimiterAndAuthorization()
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "ZARRAR", "Extensions",
            "Startup", "AppExtensions.cs"));

        var authentication = source.IndexOf("app.UseAuthentication();", StringComparison.Ordinal);
        var limiter = source.IndexOf("app.UseRateLimiter();", StringComparison.Ordinal);
        var authorization = source.IndexOf("app.UseAuthorization();", StringComparison.Ordinal);

        Assert.True(authentication >= 0, "Authentication middleware is missing.");
        Assert.True(limiter > authentication, "Rate limiting must run after authentication to partition by user.");
        Assert.True(authorization > limiter, "Authorization must run after authentication and rate limiting.");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ZARRAR.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the ZARRAR repository root.");
    }
}
