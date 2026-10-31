namespace ZARRAR.Services.SharedGateway;

/// <summary>Builds health-check targets without allowing a relative path to replace the configured authority.</summary>
public static class SharedRuntimeHealthUri
{
    private const int MaxPathLength = 128;

    public static bool TryCreate(Uri origin, string? healthPath, out Uri? target, out string? error)
    {
        target = null;
        error = null;

        if (string.IsNullOrWhiteSpace(healthPath) || healthPath.Length > MaxPathLength ||
            healthPath[0] != '/' || healthPath.StartsWith("//", StringComparison.Ordinal) ||
            healthPath.Contains("//", StringComparison.Ordinal) ||
            healthPath.Contains('%') || healthPath.Contains('\\') ||
            healthPath.Contains('?') || healthPath.Contains('#') || healthPath.Any(char.IsControl))
        {
            error = "HealthPath must be a single absolute path without encoding, query, fragment, or empty segments.";
            return false;
        }

        if (healthPath.Split('/').Any(segment => segment is "." or ".."))
        {
            error = "HealthPath must not contain traversal segments.";
            return false;
        }

        target = new Uri(origin, healthPath.TrimStart('/'));
        if (!string.Equals(target.Scheme, origin.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(target.Host, origin.Host, StringComparison.OrdinalIgnoreCase) ||
            target.Port != origin.Port || !string.IsNullOrEmpty(target.UserInfo))
        {
            target = null;
            error = "HealthPath must remain on the configured shared runtime origin.";
            return false;
        }

        return true;
    }
}
