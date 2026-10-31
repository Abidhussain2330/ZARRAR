using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using ZARRAR.Models.Internal;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Options;

namespace ZARRAR.Services.Container.Provider;

public class KubernetesMetadata : ContainerProviderMetadata
{
    /// <summary>
    /// The secret names for registry authentication
    /// </summary>
    public RegistrySet<string> AuthSecretNames { get; set; } = new();

    /// <summary>
    /// Host IP address
    /// </summary>
    public string HostIp { get; set; } = string.Empty;

    /// <summary>
    /// Kubernetes Configuration
    /// </summary>
    public KubernetesConfig Config { get; set; } = new();
}

public class KubernetesProvider : IContainerProvider<Kubernetes, KubernetesMetadata>
{
    private readonly Kubernetes _kubernetesClient;
    private readonly KubernetesMetadata _kubernetesMetadata;

    public KubernetesProvider(IOptions<RegistrySet<RegistryConfig>> registries, IOptions<ContainerProvider> options,
        ILogger<KubernetesProvider> logger)
    {
        _kubernetesMetadata = new()
        {
            Config = options.Value.KubernetesConfig ?? new(),
            PortMappingType = options.Value.PortMappingType,
            PublicEntry = options.Value.PublicEntry,
            Sandbox = options.Value.Sandbox,
            Capacity = options.Value.MaxConcurrentInstances
        };

        KubernetesClientConfiguration config;

        if (!string.IsNullOrWhiteSpace(_kubernetesMetadata.Config.KubeConfig) &&
            File.Exists(_kubernetesMetadata.Config.KubeConfig))
        {
            config = KubernetesClientConfiguration.BuildConfigFromConfigFile(_kubernetesMetadata.Config.KubeConfig);
        }
        else if (KubernetesClientConfiguration.IsInCluster())
        {
            // use ServiceAccount token if running in cluster and no kube-config is provided
            config = KubernetesClientConfiguration.InClusterConfig();
        }
        else
        {
            logger.SystemLog(StaticLocalizer[nameof(Resources.Program.ContainerProvider_KubernetesConfigLoadFailed),
                _kubernetesMetadata.Config.KubeConfig]);
            throw new FileNotFoundException(_kubernetesMetadata.Config.KubeConfig);
        }

        _kubernetesMetadata.HostIp = new Uri(config.Host).Host;
        _kubernetesClient = new Kubernetes(config);

        if (_kubernetesMetadata.Config is { PodIsolation: true, PodCidr: null or [] })
            logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.ContainerProvider_PodCidrNotConfigured)],
                TaskStatus.Pending, LogLevel.Warning);

        try
        {
            InitKubernetes(registries.Value);
        }
        catch (Exception e)
        {
            logger.LogErrorMessage(e,
                StaticLocalizer[nameof(Resources.Program.ContainerProvider_KubernetesInitFailed), config.Host]);
            ExitWithFatalMessage(
                StaticLocalizer[nameof(Resources.Program.ContainerProvider_KubernetesInitFailed), config.Host]);
        }

        logger.SystemLog(StaticLocalizer[nameof(Resources.Program.ContainerProvider_KubernetesInited), config.Host],
            TaskStatus.Success,
            LogLevel.Debug);
    }

    public Kubernetes GetProvider() => _kubernetesClient;

    public KubernetesMetadata GetMetadata() => _kubernetesMetadata;

    private void InitKubernetes(RegistrySet<RegistryConfig> registries)
    {
        if (_kubernetesClient.CoreV1.ListNamespace().Items
            .All(ns => ns.Metadata.Name != _kubernetesMetadata.Config.Namespace))
            _kubernetesClient.CoreV1.CreateNamespace(
                new() { Metadata = new() { Name = _kubernetesMetadata.Config.Namespace } });

        // Create network policies (replace if exists), once per label domain.
        //
        // A NetworkPolicy selects on one exact label key. Pods created by older releases
        // carry only the compatibility label, so a policy set keyed solely on the new
        // domain would leave those pods unrestricted for the rest of their lifetime. Applying both
        // sets is safe: policies selecting the same pod are additive, and the two sets carry
        // identical rules, so the effective permission is unchanged. See ContainerLabels.
        foreach (var labelKey in ContainerLabels.BothKeys(ContainerLabels.NetworkMode))
        {
            var suffix = labelKey.StartsWith(ContainerLabels.Domain, StringComparison.Ordinal) ? "" : "-legacy";

            EnsureNetworkPolicy(OpenNetworkPolicy(labelKey, suffix));
            EnsureNetworkPolicy(IsolatedNetworkPolicy(labelKey, suffix));

            // Custom-mode pods previously received a label but no policy at all, so they reached
            // the platform, its database and the cloud metadata service unrestricted (F-020).
            EnsureNetworkPolicy(CustomNetworkPolicy(labelKey, suffix));
        }

        if (_kubernetesMetadata.Config.PodIsolation)
            EnsureNetworkPolicy(PodIsolationPolicy);

        // create auth secrets for registries
        foreach (var registry in registries.Where(registry => registry.Value.Valid))
            InsertRegistrySecret(registry.Key, registry.Value);
    }

    private void EnsureNetworkPolicy(V1NetworkPolicy policy)
    {
        var policyName = policy.Metadata.Name;
        try
        {
            _kubernetesClient.NetworkingV1.ReplaceNamespacedNetworkPolicy(policy, policyName,
                _kubernetesMetadata.Config.Namespace);
        }
        catch
        {
            _kubernetesClient.NetworkingV1.CreateNamespacedNetworkPolicy(policy, _kubernetesMetadata.Config.Namespace);
        }
    }

    private const string IsolatedNetworkPolicyName = "zarrar-network-isolated";
    private const string OpenNetworkPolicyName = "zarrar-network-open";
    private const string CustomNetworkPolicyName = "zarrar-network-custom";
    private const string PodIsolationPolicyName = "zarrar-pod-isolation";

    private static V1LabelSelector ModeSelector(string labelKey, NetworkMode mode) =>
        new()
        {
            MatchLabels = new Dictionary<string, string>
            {
                [labelKey] = mode.ToString().ToLowerInvariant()
            }
        };

    /// <summary>
    /// Isolated Network Policy
    /// </summary>
    /// <remarks>
    ///  Blocks all outbound traffic.
    /// </remarks>
    private static V1NetworkPolicy IsolatedNetworkPolicy(string labelKey, string nameSuffix) =>
        new()
        {
            Metadata = new V1ObjectMeta { Name = IsolatedNetworkPolicyName + nameSuffix },
            Spec = new V1NetworkPolicySpec
            {
                PodSelector = ModeSelector(labelKey, NetworkMode.Isolated),
                PolicyTypes = ["Egress"],
                Egress = []
            }
        };

    /// <summary>
    ///  Open Network Policy
    /// </summary>
    /// <remarks>
    ///  Allows all outbound traffic except to the denied CIDR blocks. The default deny-list used to
    ///  be <c>10.0.0.0/8</c> alone, which left the rest of RFC1918, loopback, and the link-local
    ///  cloud-metadata address reachable from every challenge (finding F-020).
    /// </remarks>
    private V1NetworkPolicy OpenNetworkPolicy(string labelKey, string nameSuffix) =>
        RestrictedEgressPolicy(OpenNetworkPolicyName + nameSuffix, labelKey, NetworkMode.Open);

    /// <summary>
    ///  Custom Network Policy
    /// </summary>
    /// <remarks>
    ///  Custom-mode pods received no policy whatsoever, so a challenge placed on an
    ///  operator-defined network could reach the platform itself. They now carry the same egress
    ///  deny-list as Open mode (finding F-020).
    /// </remarks>
    private V1NetworkPolicy CustomNetworkPolicy(string labelKey, string nameSuffix) =>
        RestrictedEgressPolicy(CustomNetworkPolicyName + nameSuffix, labelKey, NetworkMode.Custom);

    private V1NetworkPolicy RestrictedEgressPolicy(string name, string labelKey, NetworkMode mode) =>
        new()
        {
            Metadata = new() { Name = name },
            Spec = new()
            {
                PodSelector = ModeSelector(labelKey, mode),
                PolicyTypes = ["Egress"],
                Egress =
                [
                    new V1NetworkPolicyEgressRule
                    {
                        To =
                        [
                            new V1NetworkPolicyPeer
                            {
                                IpBlock = new()
                                {
                                    Cidr = "0.0.0.0/0",
                                    Except = _kubernetesMetadata.Config.DeniedEgressCidr
                                }
                            }
                        ]
                    }
                ]
            }
        };

    /// <summary>
    ///  Pod Isolation Policy
    /// </summary>
    /// <remarks>
    ///  Both shipped policies were Egress-only, so nothing stopped one team's challenge instance
    ///  from reaching another team's instance inside the challenge namespace (finding F-020).
    ///  This policy applies to every pod in the namespace and permits ingress only from other
    ///  namespaces — which is where the platform itself runs — plus any address outside the
    ///  cluster's pod network.
    /// </remarks>
    private V1NetworkPolicy PodIsolationPolicy
    {
        get
        {
            var ns = _kubernetesMetadata.Config.Namespace;
            var podCidr = _kubernetesMetadata.Config.PodCidr;

            List<V1NetworkPolicyPeer> from =
            [
                new()
                {
                    NamespaceSelector = new V1LabelSelector
                    {
                        MatchExpressions =
                        [
                            new V1LabelSelectorRequirement
                            {
                                Key = "kubernetes.io/metadata.name", OperatorProperty = "NotIn", Values = [ns]
                            }
                        ]
                    }
                }
            ];

            // NodePort traffic arrives source-NATed from a node address rather than a pod address,
            // so it has to be admitted by ipBlock. Excluding the pod network from that block is
            // what actually separates one instance from another; without PodCidr the platform
            // cannot tell the two apart and keeps challenges reachable instead.
            from.Add(new V1NetworkPolicyPeer
            {
                IpBlock = new V1IPBlock { Cidr = "0.0.0.0/0", Except = podCidr }
            });

            return new V1NetworkPolicy
            {
                Metadata = new() { Name = PodIsolationPolicyName },
                Spec = new()
                {
                    PodSelector = new V1LabelSelector(),
                    PolicyTypes = ["Ingress"],
                    Ingress = [new V1NetworkPolicyIngressRule { FromProperty = from }]
                }
            };
        }
    }

    private void InsertRegistrySecret(string address, RegistryConfig registry)
    {
        var padding = $"ZARRAR@{registry.UserName}@{address}".ToMD5String();
        var secretName = $"{registry.UserName}-{padding}".ToValidRFC1123String("secret");

        var auth = Codec.Base64.Encode($"{registry.UserName}:{registry.Password}");
        var dockerJsonObj = new DockerRegistryOptions(
            new Dictionary<string, DockerRegistryEntry> { [address] = new(auth, registry.UserName, registry.Password) }
        );

        var dockerJsonBytes =
            JsonSerializer.SerializeToUtf8Bytes(dockerJsonObj, AppJsonSerializerContext.Default.DockerRegistryOptions);
        var secret = new V1Secret
        {
            Metadata =
                new V1ObjectMeta { Name = secretName, NamespaceProperty = _kubernetesMetadata.Config.Namespace },
            Data = new Dictionary<string, byte[]> { [".dockerconfigjson"] = dockerJsonBytes },
            Type = "kubernetes.io/dockerconfigjson"
        };

        try
        {
            _kubernetesClient.CoreV1.ReplaceNamespacedSecret(secret, secretName,
                _kubernetesMetadata.Config.Namespace);
        }
        catch
        {
            _kubernetesClient.CoreV1.CreateNamespacedSecret(secret, _kubernetesMetadata.Config.Namespace);
        }

        if (!_kubernetesMetadata.AuthSecretNames.TryAdd(address, secretName))
            _kubernetesMetadata.AuthSecretNames[address] = secretName;
    }
}

[SuppressMessage("ReSharper", "InconsistentNaming")]
internal record DockerRegistryOptions(Dictionary<string, DockerRegistryEntry> auths);

[SuppressMessage("ReSharper", "InconsistentNaming")]
internal record DockerRegistryEntry(string auth, string? username, string? password);
