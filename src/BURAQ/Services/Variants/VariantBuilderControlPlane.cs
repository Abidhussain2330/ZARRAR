using BURAQ.Models.Internal;

namespace BURAQ.Services.Variants;

/// <summary>One fail-closed validation policy shared by startup, status, and worker dispatch.</summary>
public static class VariantBuilderControlPlane
{
    public static bool TryValidate(VariantBuilderOptions options, out Uri? origin)
    {
        origin = null;
        var token = options.ApiToken;
        if (string.IsNullOrWhiteSpace(token) || token.Length < 32 || token.Any(char.IsWhiteSpace) ||
            token.Contains("REQUIRED", StringComparison.OrdinalIgnoreCase) ||
            token.Contains("REPLACE_WITH", StringComparison.OrdinalIgnoreCase) ||
            options.Parallelism is < 1 or > 16 ||
            options.MaxArtifactBytes is < 1024 or > 536870912)
            return false;

        return Uri.TryCreate(options.ControlPlaneOrigin, UriKind.Absolute, out origin) &&
               origin.Scheme == Uri.UriSchemeHttps && origin.AbsolutePath == "/" &&
               !string.IsNullOrWhiteSpace(origin.Host) && string.IsNullOrEmpty(origin.UserInfo) &&
               string.IsNullOrEmpty(origin.Query) && string.IsNullOrEmpty(origin.Fragment);
    }
}
