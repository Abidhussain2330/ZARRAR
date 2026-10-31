using System.Text;
using Xunit;

namespace ZARRAR.Test;

public sealed class BrandingIsolationTests
{
    [Fact]
    public void Repository_uses_only_the_ZARRAR_platform_identifier()
    {
        var root = FindRepositoryRoot();

        Assert.True(File.Exists(Path.Combine(root, string.Concat("ZA", "RRAR", ".sln"))));

        var legacyIdentifier = string.Concat("BU", "RAQ");
        var violations = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(IsSourceFile)
            .Where(path => !path.EndsWith("LICENSE.txt", StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path, Encoding.UTF8)
                .Contains(legacyIdentifier, StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(root, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(violations);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, string.Concat("BU", "RAQ", ".sln")))
                || File.Exists(Path.Combine(directory.FullName, string.Concat("ZA", "RRAR", ".sln"))))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private static bool IsSourceFile(string path)
    {
        var relative = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        if (relative.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relative.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relative.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relative.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            return false;

        return Path.GetExtension(path) is ".cs" or ".csproj" or ".sln" or ".md" or ".txt" or ".json"
            or ".yaml" or ".yml" or ".ps1" or ".cmd" or ".sh" or ".props" or ".mjs" or ".mts"
            or ".ts" or ".tsx" or ".css" or ".html" or ".resx" or ".xml";
    }
}
