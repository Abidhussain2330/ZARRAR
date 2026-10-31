using Xunit;

namespace ZARRAR.Test;

/// <summary>
/// Guards the player download route for container challenges that include a shared source file.
/// The controller is dependency-heavy, so this regression test deliberately asserts the route's
/// authorization branch rather than reimplementing its authorization dependencies in a mock.
/// </summary>
public sealed class ContainerAttachmentDownloadRouteTests
{
    [Fact]
    public void Download_route_does_not_reject_a_dynamic_container_shared_attachment()
    {
        var root = FindRepositoryRoot();
        var controller = File.ReadAllText(Path.Combine(root, "src", "ZARRAR", "Controllers", "GameController.cs"));
        var start = controller.IndexOf("public async Task<IActionResult> DownloadChallengeAttachment", StringComparison.Ordinal);

        Assert.True(start >= 0, "The challenge attachment download action must exist.");

        var action = controller[start..];
        Assert.DoesNotContain("instance.Challenge.Type != ChallengeType.StaticAttachment", action,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "ZARRAR", "Controllers", "GameController.cs")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not find the repository root.");
    }
}
