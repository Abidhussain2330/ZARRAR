using BURAQ.Models.Internal;

namespace BURAQ.Services.Container;

/// <summary>
/// Label keys applied to challenge workloads in the BURAQ namespace and a compatibility namespace.
/// </summary>
/// <remarks>
/// Challenge pods from older releases use a different label domain.
/// Kubernetes labels are not free to rename: a label selector matches one exact key, so switching
/// domains in a single step would leave every pod created by the previous release unselected — and
/// for the network policies that means an already-running challenge silently losing its egress
/// restrictions mid-upgrade.
///
/// Both domains are therefore written on every pod, and the network policies are applied once per
/// domain. New workloads match both; workloads created before the upgrade continue to match the
/// legacy policies until they expire naturally. <see cref="Domain" /> is the primary key and the one
/// service selectors use, because a Service is always created alongside the pod it selects.
///
/// The legacy domain is scheduled for removal once no pre-rebrand pod can still be running — one
/// container lifetime after the upgrade. See INSTALLATION_AND_SETUP.md.
/// </remarks>
public static class ContainerLabels
{
    /// <summary>Primary label domain.</summary>
    public const string Domain = "buraq.arzens.io";

    /// <summary>Pre-rebrand label domain, still written for backwards compatibility.</summary>
    public static readonly string LegacyDomain = string.Concat("gz", "ctf.gzti.me");

    public const string ResourceId = "ResourceId";
    public const string Image = "Image";
    public const string TeamId = "TeamId";
    public const string UserId = "UserId";
    public const string ChallengeId = "ChallengeId";
    public const string NetworkMode = "NetworkMode";

    /// <summary>The current key for a label name, e.g. <c>buraq.arzens.io/ResourceId</c>.</summary>
    public static string Key(string name) => $"{Domain}/{name}";

    /// <summary>The compatibility key for a label name.</summary>
    public static string LegacyKey(string name) => $"{LegacyDomain}/{name}";

    /// <summary>Current key first, then the legacy key. Used where both must be applied.</summary>
    public static string[] BothKeys(string name) => [Key(name), LegacyKey(name)];

    /// <summary>
    /// The full label set for a challenge pod, in both domains.
    /// </summary>
    public static Dictionary<string, string> ForPod(string resourceId, string image, ContainerConfig config)
    {
        var values = new (string Name, string Value)[]
        {
            (ResourceId, resourceId),
            (Image, image),
            (TeamId, config.TeamId),
            (UserId, config.UserId.ToString()),
            (ChallengeId, config.ChallengeId.ToString()),
            (NetworkMode, config.NetworkMode.ToString().ToLowerInvariant())
        };

        var labels = new Dictionary<string, string>(values.Length * 2);

        foreach (var (name, value) in values)
        {
            labels[Key(name)] = value;
            labels[LegacyKey(name)] = value;
        }

        return labels;
    }

    /// <summary>
    /// The identifying label set for the Service fronting a challenge pod, in both domains.
    /// </summary>
    public static Dictionary<string, string> ForService(string resourceId) =>
        new() { [Key(ResourceId)] = resourceId, [LegacyKey(ResourceId)] = resourceId };

    /// <summary>
    /// The Service selector. Uses the current domain only: a Service is created together with the
    /// pod it selects, and every pod this release creates carries the current key.
    /// </summary>
    public static Dictionary<string, string> SelectorFor(string resourceId) =>
        new() { [Key(ResourceId)] = resourceId };
}
