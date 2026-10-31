using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using ZARRAR.Extensions;
using ZARRAR.Services.Cache;
using MemoryPack;
using Microsoft.Extensions.Options;
using OpenTelemetry.Exporter;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Utilities.Encoders;
using Serilog.Sinks.Grafana.Loki;

namespace ZARRAR.Models.Internal;

/// <summary>
/// Ignore when saving automatically
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AutoSaveIgnoreAttribute : Attribute;

/// <summary>
/// Update cache when this property changes
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public sealed class CacheFlushAttribute(string cacheKey) : Attribute
{
    public string CacheKey { get; } = cacheKey;
}

/// <summary>
/// Account policy
/// </summary>
public class AccountPolicy
{
    /// <summary>
    /// Allow user registration
    /// </summary>
    [CacheFlush(CacheKey.ClientConfig)]
    public bool AllowRegister { get; set; } = true;

    /// <summary>
    /// Activate account upon registration
    /// </summary>
    public bool ActiveOnRegister { get; set; } = true;

    /// <summary>
    /// Use captcha verification
    /// </summary>
    [CacheFlush(CacheKey.CaptchaConfig)]
    public bool UseCaptcha { get; set; }

    /// <summary>
    /// Email confirmation required for registration, email change, and password recovery
    /// </summary>
    public bool EmailConfirmationRequired { get; set; }

    /// <summary>
    /// Email domain list, separated by commas
    /// </summary>
    public string EmailDomainList { get; set; } = string.Empty;
}

/// <summary>
/// Container policy
/// </summary>
public class ContainerPolicy
{
    /// <summary>
    /// Automatically destroy the oldest container when the limit is reached
    /// </summary>
    public bool AutoDestroyOnLimitReached { get; set; }

    /// <summary>
    /// User container limit, used to limit the number of exercise containers
    /// </summary>
    public int MaxExerciseContainerCountPerUser { get; set; } = 1;

    /// <summary>
    /// Default container lifetime in minutes
    /// </summary>
    [CacheFlush(CacheKey.ClientConfig)]
    [Range(1, 7200, ErrorMessageResourceName = nameof(Resources.Program.Model_OutOfRange),
        ErrorMessageResourceType = typeof(Resources.Program))]
    public int DefaultLifetime { get; set; } = 120;

    /// <summary>
    /// Extension duration for each renewal in minutes
    /// </summary>
    [CacheFlush(CacheKey.ClientConfig)]
    [Range(1, 7200, ErrorMessageResourceName = nameof(Resources.Program.Model_OutOfRange),
        ErrorMessageResourceType = typeof(Resources.Program))]
    public int ExtensionDuration { get; set; } = 120;

    /// <summary>
    /// Renewal window before container stops in minutes
    /// </summary>
    [CacheFlush(CacheKey.ClientConfig)]
    [Range(1, 360, ErrorMessageResourceName = nameof(Resources.Program.Model_OutOfRange),
        ErrorMessageResourceType = typeof(Resources.Program))]
    public int RenewalWindow { get; set; } = 10;
}

public class X25519KeyPair
{
    /// <summary>
    /// Public key
    /// </summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>
    /// Private key
    /// </summary>
    public string PrivateKey { get; set; } = string.Empty;

    public void RegenerateKeys(byte[] xorKey)
    {
        var kp = CryptoUtils.GenerateX25519KeyPair();
        var privateKey = (X25519PrivateKeyParameters)kp.Private;
        var publicKey = (X25519PublicKeyParameters)kp.Public;
        var privateKeyBytes = SecretProtector.Protect(privateKey.GetEncoded(), xorKey);
        PublicKey = Base64.ToBase64String(publicKey.GetEncoded());
        PrivateKey = Base64.ToBase64String(privateKeyBytes);
    }

    public string? Decrypt(string data, byte[] xorKey)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(xorKey);

        try
        {
            var encryptedData = Base64.Decode(data);
            var privateKeyBytes = SecretProtector.Unprotect(Base64.Decode(PrivateKey), xorKey);
            var privateKey = new X25519PrivateKeyParameters(privateKeyBytes);

            return Encoding.UTF8.GetString(CryptoUtils.DecryptData(encryptedData, privateKey));
        }
        catch
        {
            // If decryption fails, return null
            return null;
        }
    }
}

public class Ed25519KeyPair
{
    /// <summary>
    /// Public key
    /// </summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>
    /// Private key
    /// </summary>
    public string PrivateKey { get; set; } = string.Empty;

    public void RegenerateKeys(byte[] xorKey)
    {
        var kp = CryptoUtils.GenerateEd25519KeyPair();
        var privateKey = (Ed25519PrivateKeyParameters)kp.Private;
        var publicKey = (Ed25519PublicKeyParameters)kp.Public;
        var privateKeyBytes = SecretProtector.Protect(privateKey.GetEncoded(), xorKey);
        PublicKey = Base64.ToBase64String(publicKey.GetEncoded());
        PrivateKey = Base64.ToBase64String(privateKeyBytes);
    }

    public string Sign(string data, byte[] xorKey, bool useUrlSafeBase64 = false)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(xorKey);

        var privateKeyBytes = SecretProtector.Unprotect(Base64.Decode(PrivateKey), xorKey);
        var privateKey = new Ed25519PrivateKeyParameters(privateKeyBytes);
        return CryptoUtils.GenerateSignature(data, privateKey, SignAlgorithm.Ed25519, useUrlSafeBase64);
    }

    public bool Verify(string data, string signature, bool useUrlSafeBase64 = false)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(signature);

        try
        {
            var publicKey = new Ed25519PublicKeyParameters(Base64.Decode(PublicKey));
            return CryptoUtils.VerifySignature(data, signature, publicKey, SignAlgorithm.Ed25519, useUrlSafeBase64);
        }
        catch
        {
            // If verification fails, return false
            return false;
        }
    }
}

/// <summary>
/// A context for signature operations, including signing and verifying
/// </summary>
public record SignatureContext(Ed25519KeyPair EncryptedKeyPair, byte[] XorKey)
{
    public string Sign(string data, bool urlSafe = true) =>
        EncryptedKeyPair.Sign(data, XorKey, urlSafe);

    public bool Verify(string data, string signature, bool urlSafe = true) =>
        EncryptedKeyPair.Verify(data, signature, urlSafe);
}

/// <summary>
/// Configs controlled by the backend
/// </summary>
public class ManagedConfig
{
    /// <summary>
    /// Api encryption configuration
    /// </summary>
    public X25519KeyPair ApiEncryption { get; set; } = new();

    /// <summary>
    /// Api token configuration
    /// </summary>
    public Ed25519KeyPair ApiToken { get; set; } = new();
}

/// <summary>
/// Global settings
/// </summary>
public class GlobalConfig
{
    /// <summary>
    /// Default site description
    /// </summary>
    public const string DefaultDescription =
        "A full-spectrum CTF and cyber range platform for Red, Blue and Purple Team operations.";

    /// <summary>
    /// Platform name
    /// </summary>
    [CacheFlush(CacheKey.Index)]
    [CacheFlush(CacheKey.ClientConfig)]
    public string Title { get; set; } = "ZARRAR";

    /// <summary>
    /// Platform slogan
    /// </summary>
    [CacheFlush(CacheKey.ClientConfig)]
    public string Slogan { get; set; } = "Hack for fun not for profit";

    /// <summary>
    /// Site description information
    /// </summary>
    [CacheFlush(CacheKey.Index)]
    public string? Description { get; set; } = DefaultDescription;

    /// <summary>
    /// Footer information
    /// </summary>
    [CacheFlush(CacheKey.ClientConfig)]
    public string? FooterInfo { get; set; }

    /// <summary>
    /// Custom theme color
    /// </summary>
    [CacheFlush(CacheKey.ClientConfig)]
    public string? CustomTheme { get; set; }

    /// <summary>
    /// Canonical public origin of the platform, e.g. https://ctf.example.com
    /// Used to build security-sensitive email links so that they never derive from
    /// the client-supplied Host header. Leave empty only for local development.
    /// </summary>
    public string? PublicUrl { get; set; }

    /// <summary>
    /// Use asymmetric encryption for API requests
    /// </summary>
    [CacheFlush(CacheKey.ClientConfig)]
    public bool ApiEncryption { get; set; }

    /// <summary>
    /// Platform logo hash
    /// </summary>
    [AutoSaveIgnore]
    public string? LogoHash { get; set; }

    /// <summary>
    /// Platform favicon hash
    /// </summary>
    [AutoSaveIgnore]
    public string? FaviconHash { get; set; }

    [JsonIgnore]
    public string? LogoUrl => string.IsNullOrEmpty(LogoHash) ? null : $"/assets/{LogoHash}/logo";

    /// <summary>
    /// Platform name, used for email and homepage rendering.
    /// The operator-configured <see cref="Title" /> is used verbatim: the product name is
    /// simply "ZARRAR", never a "::CTF"-suffixed construction.
    /// </summary>
    [JsonIgnore]
    public string Platform => string.IsNullOrEmpty(Title) ? "ZARRAR" : Title;
}

/// <summary>
/// Client configuration
/// </summary>
[MemoryPackable]
public partial class ClientConfig
{
    /// <summary>
    /// Whether anonymous visitors may create accounts.
    /// </summary>
    public bool AllowRegister { get; set; } = true;

    /// <summary>
    /// Platform name
    /// </summary>
    public string Title { get; set; } = "ZARRAR";

    /// <summary>
    /// Platform slogan
    /// </summary>
    public string Slogan { get; set; } = "Hack for fun not for profit";

    /// <summary>
    /// Footer information
    /// </summary>
    public string? FooterInfo { get; set; }

    /// <summary>
    /// Custom theme color
    /// </summary>
    public string? CustomTheme { get; set; }

    /// <summary>
    /// The public key used for API requests
    /// </summary>
    public string? ApiPublicKey { get; set; }

    /// <summary>
    /// Platform logo URL
    /// </summary>
    public string? LogoUrl { get; set; }

    /// <summary>
    /// Container port mapping type
    /// </summary>
    public ContainerPortMappingType PortMapping { get; set; } = ContainerPortMappingType.Default;

    /// <summary>
    /// Default container lifetime in minutes
    /// </summary>
    public int DefaultLifetime { get; set; } = 120;

    /// <summary>
    /// Extension duration for each renewal in minutes
    /// </summary>
    public int ExtensionDuration { get; set; } = 120;

    /// <summary>
    /// Renewal window before container stops in minutes
    /// </summary>
    public int RenewalWindow { get; set; } = 10;

    [JsonIgnore]
    public DateTimeOffset UpdateTimeUtc { get; set; } = DateTimeOffset.UtcNow;

    public static ClientConfig FromServiceProvider(IServiceProvider serviceProvider) =>
        FromConfigs(
            serviceProvider.GetRequiredService<IOptionsSnapshot<GlobalConfig>>().Value,
            serviceProvider.GetRequiredService<IOptionsSnapshot<AccountPolicy>>().Value,
            serviceProvider.GetRequiredService<IOptionsSnapshot<ContainerPolicy>>().Value,
            serviceProvider.GetRequiredService<IOptionsSnapshot<ContainerProvider>>().Value,
            serviceProvider.GetRequiredService<IOptionsSnapshot<ManagedConfig>>().Value);

    private static ClientConfig FromConfigs(GlobalConfig globalConfig, AccountPolicy accountPolicy,
        ContainerPolicy containerPolicy,
        ContainerProvider containerProvider, ManagedConfig managedConfig) =>
        new()
        {
            Title = globalConfig.Title,
            Slogan = globalConfig.Slogan,
            FooterInfo = globalConfig.FooterInfo,
            CustomTheme = globalConfig.CustomTheme,
            AllowRegister = accountPolicy.AllowRegister,
            LogoUrl = globalConfig.LogoUrl,
            ApiPublicKey = globalConfig.ApiEncryption ? managedConfig.ApiEncryption.PublicKey : null,
            PortMapping = containerProvider.PortMappingType,
            DefaultLifetime = containerPolicy.DefaultLifetime,
            ExtensionDuration = containerPolicy.ExtensionDuration,
            RenewalWindow = containerPolicy.RenewalWindow
        };
}

#region Mail Config

public class SmtpConfig
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 587;
    public bool BypassCertVerify { get; set; }
}

public class EmailConfig
{
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? SenderAddress { get; set; } = string.Empty;
    public string? SenderName { get; set; } = string.Empty;
    public SmtpConfig? Smtp { get; set; } = new();
}

#endregion

#region Container Provider

[JsonConverter(typeof(JsonStringEnumConverter<ContainerProviderType>))]
public enum ContainerProviderType
{
    Docker,
    Kubernetes,
    MicroVm
}

[JsonConverter(typeof(JsonStringEnumConverter<ContainerPortMappingType>))]
public enum ContainerPortMappingType
{
    /// Use default to map the container port to a random port on the host
    Default,

    /// Use platform proxy to map the container tcp to wss
    PlatformProxy
}

public class ContainerProvider
{
    public ContainerProviderType Type { get; set; } = ContainerProviderType.Docker;
    public ContainerPortMappingType PortMappingType { get; set; } = ContainerPortMappingType.Default;
    public bool EnableTrafficCapture { get; set; }
    public string PublicEntry { get; set; } = string.Empty;
    public KubernetesConfig? KubernetesConfig { get; set; }
    public DockerConfig? DockerConfig { get; set; }
    public MicroVmConfig? MicroVmConfig { get; set; }

    /// <summary>Provider-wide workload capacity. Zero delegates the limit to the provider.</summary>
    public int MaxConcurrentInstances { get; set; }

    /// <summary>
    /// Mandatory sandbox baseline applied to every challenge workload
    /// </summary>
    public ContainerSandboxConfig Sandbox { get; set; } = new();

    /// <summary>
    /// Network mode used for challenges that do not specify one
    /// </summary>
    /// <remarks>
    /// Was effectively <see cref="NetworkMode.Open" /> (finding F-020). New challenges now
    /// default to <see cref="NetworkMode.Isolated" />; challenges that already carry an explicit
    /// value are unaffected, and existing NULL rows are backfilled to Open by migration so no
    /// running competition changes behaviour on upgrade.
    /// </remarks>
    public NetworkMode DefaultNetworkMode { get; set; } = NetworkMode.Isolated;
}

/// <summary>
/// Sandbox hardening applied to challenge containers and pods (finding F-003).
/// </summary>
/// <remarks>
/// The defaults are chosen to be a strict improvement that does not break ordinary challenge
/// images. The three controls that commonly *do* break real challenges —
/// <see cref="NoNewPrivileges" /> (breaks the setuid pattern used by many pwn challenges),
/// <see cref="RunAsNonRoot" /> and <see cref="ReadOnlyRootFilesystem" /> — are off by default and
/// must be enabled deliberately after validating each challenge image.
/// </remarks>
public class ContainerSandboxConfig
{
    /// <summary>
    /// Master switch for the whole sandbox baseline
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Linux capabilities removed from the workload
    /// </summary>
    public string[] DropCapabilities { get; set; } = ["ALL"];

    /// <summary>
    /// Capabilities granted back after the drop; the minimum most images need to start and to
    /// drop to an unprivileged user themselves
    /// </summary>
    public string[] AddCapabilities { get; set; } =
        ["CHOWN", "DAC_OVERRIDE", "FOWNER", "SETGID", "SETUID", "KILL", "NET_BIND_SERVICE"];

    /// <summary>
    /// Forbid privilege gain through setuid binaries. Off by default: setuid is a legitimate and
    /// widespread challenge design.
    /// </summary>
    public bool NoNewPrivileges { get; set; }

    /// <summary>
    /// Mount the root filesystem read-only. Off by default: most challenge images write to disk.
    /// </summary>
    public bool ReadOnlyRootFilesystem { get; set; }

    /// <summary>
    /// Refuse to start workloads whose image runs as UID 0. Off by default: many challenge images
    /// legitimately require root inside their own namespace.
    /// </summary>
    public bool RunAsNonRoot { get; set; }

    /// <summary>
    /// Maximum number of processes, which bounds fork-bomb denial of service. Zero disables.
    /// </summary>
    public int PidsLimit { get; set; } = 512;

    /// <summary>
    /// Seccomp profile applied to Kubernetes pods. Docker applies its built-in default profile
    /// unless explicitly unconfined, so this value is Kubernetes-only.
    /// </summary>
    public string SeccompProfile { get; set; } = "RuntimeDefault";

    /// <summary>
    /// Enforce <c>ContainerConfig.StorageLimit</c> on Docker. Off by default because the Docker
    /// daemon only honours it on specific storage drivers (overlay2 on xfs with pquota,
    /// btrfs, zfs) and rejects container creation elsewhere.
    /// </summary>
    public bool EnableStorageLimit { get; set; }
}

public class DockerConfig
{
    public string Uri { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string? ChallengeNetwork { get; set; }
}

/// <summary>Trusted control-plane configuration for Firecracker/Cloud Hypervisor workloads.</summary>
public class MicroVmConfig
{
    public string ControlPlaneOrigin { get; set; } = string.Empty;
    public string ApiToken { get; set; } = string.Empty;
    public string NodeName { get; set; } = "microvm-primary";
    public string HealthPath { get; set; } = "v1/health";
    public int RequestTimeoutSeconds { get; set; } = 30;
}

public class KubernetesConfig
{
    /// <summary>
    /// Egress destinations denied to challenge pods.
    /// </summary>
    /// <remarks>
    /// Despite the historical name this is the <c>except</c> list of the egress policy — the set of
    /// destinations a challenge may <b>not</b> reach. It previously defaulted to
    /// <c>10.0.0.0/8</c> only, which left the platform, the database, the cache and the cloud
    /// metadata service reachable on most clusters (finding F-020).
    /// </remarks>
    public static readonly string[] DefaultDeniedEgressCidr =
    [
        "0.0.0.0/8", // "this network"
        "10.0.0.0/8", // RFC1918
        "100.64.0.0/10", // RFC6598 carrier-grade NAT
        "127.0.0.0/8", // loopback
        "169.254.0.0/16", // link-local, includes 169.254.169.254 cloud metadata
        "172.16.0.0/12", // RFC1918
        "192.0.0.0/24", // IETF protocol assignments
        "192.168.0.0/16", // RFC1918
        "198.18.0.0/15", // benchmarking
        "224.0.0.0/4", // multicast
        "240.0.0.0/4" // reserved
    ];

    public string Namespace { get; set; } = "zarrar-challenges";
    public string KubeConfig { get; set; } = "kube-config.yaml";
    public string[]? AllowCidr { get; set; }
    public string[]? Dns { get; set; }

    /// <summary>
    /// Deny challenge pods from reaching each other, so that one team cannot attack or observe
    /// another team's instance.
    /// </summary>
    public bool PodIsolation { get; set; } = true;

    /// <summary>
    /// The cluster's pod network CIDRs.
    /// </summary>
    /// <remarks>
    /// Kubernetes NetworkPolicy can only exclude pod-to-pod traffic by source address, so
    /// <see cref="PodIsolation" /> is only fully effective once this is set. When it is unset the
    /// platform still isolates by namespace and logs a warning at startup, because narrowing the
    /// ingress ipBlock without knowing the pod CIDR would also block NodePort traffic and make
    /// challenges unreachable.
    /// </remarks>
    public string[]? PodCidr { get; set; }

    /// <summary>
    /// Effective egress deny-list, falling back to the hardened default.
    /// </summary>
    public IList<string> DeniedEgressCidr =>
        AllowCidr is { Length: > 0 } ? AllowCidr : DefaultDeniedEgressCidr;
}

public class RegistrySet<T> : Dictionary<string, T>
    where T : class
{
    public T? GetForImage(string image)
    {
        if (string.IsNullOrWhiteSpace(image))
            return null;

        image = image.Contains("://") ? image : $"https://{image}";

        if (!Uri.TryCreate(image, UriKind.Absolute, out var uri) || uri.HostNameType == UriHostNameType.Unknown)
            return null;

        return TryGetValue(uri.Authority, out var cfg) ? cfg :
            TryGetValue(uri.Host, out var cfgHost) ? cfgHost : null;
    }
}

public class RegistryConfig
{
    public string? ServerAddress { get; set; }
    public string? UserName { get; set; }
    public string? Password { get; set; }

    public bool Valid => !string.IsNullOrEmpty(UserName) &&
                         !string.IsNullOrEmpty(Password);
}

#endregion

#region Captcha Provider

[JsonConverter(typeof(JsonStringEnumConverter<CaptchaProvider>))]
public enum CaptchaProvider
{
    None,
    HashPow,
    CloudflareTurnstile
}

public class HashPowConfig
{
    // How many leading zeros the hash should have
    private int _difficulty = 18;

    public int Difficulty
    {
        set => _difficulty = value;
        get => _difficulty = Math.Clamp(_difficulty, 8, 48);
    }
}

public class CaptchaConfig
{
    public CaptchaProvider Provider { get; set; }
    public string? SecretKey { get; set; }
    public string? SiteKey { get; set; }
    public HashPowConfig HashPow { get; set; } = new();
}

#endregion

#region Telemetry

public class TelemetryConfig
{
    public PrometheusConfig Prometheus { get; set; } = new();
    public OpenTelemetryConfig OpenTelemetry { get; set; } = new();
    public AzureMonitorConfig AzureMonitor { get; set; } = new();
    public ConsoleConfig Console { get; set; } = new();

    [JsonIgnore]
    public bool Enable => Prometheus.Enable || OpenTelemetry.Enable || AzureMonitor.Enable || Console.Enable;
}

public class PrometheusConfig
{
    public bool Enable { get; set; }
    public bool TotalNameSuffixForCounters { get; set; }

    /// <summary>
    /// Bearer token required to scrape <c>/metrics</c>.
    /// </summary>
    /// <remarks>
    /// The scrape endpoint was gated only on the listening port, so anything that could reach the
    /// management listener could read runtime, process, database and cache metrics without
    /// authenticating (finding F-019). Leave unset to keep the port-only behaviour, which is
    /// sufficient once the listener is bound to loopback. <c>/healthz</c> is deliberately not
    /// covered, so container and orchestrator probes keep working unchanged.
    /// </remarks>
    public string? ScrapeToken { get; set; }
}

public class OpenTelemetryConfig
{
    public bool Enable { get; set; }
    public OtlpExportProtocol Protocol { get; set; }
    public string? EndpointUri { get; set; }
}

public class AzureMonitorConfig
{
    public bool Enable { get; set; }
    public string? ConnectionString { get; set; }
}

public class ConsoleConfig
{
    public bool Enable { get; set; }
}

#endregion

public class GrafanaLokiOptions
{
    public bool Enable { get; set; }
    public string? EndpointUri { get; set; }
    public LokiLabel[]? Labels { get; set; }
    public string[]? PropertiesAsLabels { get; set; }
    public LokiCredentials? Credentials { get; set; }
    public string? Tenant { get; set; }
    public LogLevel? MinimumLevel { get; set; }
}

public class ForwardedOptions : ForwardedHeadersOptions
{
    // For historical configuration compatibility as we accept string
    public new List<string>? KnownIPNetworks { get; set; }
    public new List<string>? KnownProxies { get; set; }

    // Old properties for compatibility
    public new List<string>? KnownNetworks { get; set; }
    public List<string>? TrustedNetworks { get; set; }
    public List<string>? TrustedProxies { get; set; }

    public void ToForwardedHeadersOptions(ForwardedHeadersOptions options)
    {
        // assign the same value to the base class via reflection
        var type = typeof(ForwardedHeadersOptions);
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var property in properties)
        {
            // skip the properties that are not being set directly
            // .NET 10 update: `KnownNetworks` is obsolete, needs to be skipped
            if (property.Name is nameof(KnownIPNetworks) or nameof(KnownProxies) or "KnownNetworks")
                continue;

            property.SetValue(options, property.GetValue(this));
        }

        // Handle KnownIPNetworks
        Action<string> addNetwork = networkString =>
        {
            // split the network into address and prefix length
            var parts = networkString.Split('/');
            if (parts.Length == 2 &&
                IPAddress.TryParse(parts[0], out var prefix) &&
                int.TryParse(parts[1], out var prefixLength))
                options.KnownIPNetworks.Add(new IPNetwork(prefix, prefixLength));
        };

        KnownIPNetworks?.ForEach(addNetwork);
        KnownNetworks?.ForEach(addNetwork);
        TrustedNetworks?.ForEach(addNetwork);

        // Handle KnownProxies
        Action<string> addProxies = proxy =>
            Array.ForEach(proxy.ResolveIP(), ip => options.KnownProxies.Add(ip));

        KnownProxies?.ForEach(addProxies);
        TrustedProxies?.ForEach(addProxies);
    }
}
