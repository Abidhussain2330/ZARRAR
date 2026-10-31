using Xunit;

namespace ZARRAR.Test;

public sealed class AspireDevelopmentContractTests
{
    [Fact]
    public void AppHostPinsAuthenticatedPlatformProxyForDevelopment()
    {
        var root = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(root, "src", "ZARRAR.AppHost", "Program.cs"));

        Assert.Contains(".WithEnvironment(\"ContainerProvider__PortMappingType\", \"PlatformProxy\")", program,
            StringComparison.Ordinal);
        Assert.Contains(".WithEnvironment(\"ContainerProvider__EnableTrafficCapture\", \"true\")", program,
            StringComparison.Ordinal);
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
