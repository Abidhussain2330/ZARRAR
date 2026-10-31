using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ZARRAR.Services.Variants;

public sealed record VariantManifestValidation(bool IsValid, string ManifestHash, string[] Errors);

public sealed partial class VariantManifestValidator
{
    [GeneratedRegex("^[a-z0-9][a-z0-9._/-]{0,127}@sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex PinnedImageRegex();

    public VariantManifestValidation Validate(string manifestJson, string builderImageDigest,
        string outputFileName)
    {
        var errors = new List<string>();
        if (!PinnedImageRegex().IsMatch(builderImageDigest))
            errors.Add("Builder image must be pinned to an exact sha256 digest.");
        if (string.IsNullOrWhiteSpace(outputFileName) || outputFileName.Length > 256 ||
            outputFileName.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(outputFileName))
            errors.Add("Output filename must be a bounded relative path without traversal.");

        try
        {
            using var document = JsonDocument.Parse(manifestJson, new JsonDocumentOptions
                { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                errors.Add("Manifest root must be an object.");
            else
            {
                if (!document.RootElement.TryGetProperty("entrypoint", out var entrypoint) ||
                    entrypoint.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(entrypoint.GetString()))
                    errors.Add("Manifest requires a non-empty entrypoint.");
                if (!document.RootElement.TryGetProperty("schemaVersion", out var version) ||
                    version.ValueKind != JsonValueKind.Number || version.GetInt32() != 1)
                    errors.Add("Manifest schemaVersion must equal 1.");
                if (document.RootElement.TryGetProperty("network", out var network) &&
                    network.ValueKind == JsonValueKind.True)
                    errors.Add("Variant builds cannot request network access.");
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            errors.Add("Manifest is not valid bounded JSON.");
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(manifestJson)));
        return new VariantManifestValidation(errors.Count == 0, hash, errors.ToArray());
    }
}
