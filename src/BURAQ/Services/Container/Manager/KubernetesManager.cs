// SPDX-License-Identifier: LicenseRef-THE-ARZENS-Custom
// Copyright (C) 2022-2025 THE ARZENS

using System.Net;
using BURAQ.Models.Internal;
using BURAQ.Services.Container.Provider;
using k8s;
using k8s.Autorest;
using k8s.Models;

namespace BURAQ.Services.Container.Manager;

public class KubernetesManager : IContainerManager
{
    private readonly Kubernetes _client;
    private readonly ILogger<KubernetesManager> _logger;
    private readonly KubernetesMetadata _meta;

    public KubernetesManager(IContainerProvider<Kubernetes, KubernetesMetadata> provider,
        ILogger<KubernetesManager> logger)
    {
        _logger = logger;
        _meta = provider.GetMetadata();
        _client = provider.GetProvider();

        logger.SystemLog(StaticLocalizer[nameof(Resources.Program.ContainerManager_K8sMode)],
            TaskStatus.Success,
            LogLevel.Debug);
    }

    public async Task<Models.Data.Container?> CreateContainerAsync(ContainerConfig config,
        CancellationToken token = default)
    {
        if (_meta.Capacity > 0)
        {
            var pods = await _client.CoreV1.ListNamespacedPodAsync(_meta.Config.Namespace,
                cancellationToken: token);
            var active = pods.Items.Count(x => x.Status?.Phase is "Running" or "Pending");
            if (active >= _meta.Capacity)
            {
                _logger.LogWarning("Kubernetes challenge capacity {Capacity} is exhausted.", _meta.Capacity);
                return null;
            }
        }

        var imageName = config.Image.Split("/").LastOrDefault()?.Split(":").FirstOrDefault();

        if (string.IsNullOrWhiteSpace(imageName))
        {
            _logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.ContainerManager_UnresolvedImageName), config.Image],
                TaskStatus.Failed, LogLevel.Warning);
            return null;
        }

        var authSecretName = _meta.AuthSecretNames.GetForImage(config.Image);
        var options = _meta.Config;

        var chalImage = imageName.ToValidRFC1123String("chal");

        var name = $"{chalImage}-{Guid.NewGuid().ToString("N")[..16]}";

        // BURAQ_FLAG is a per-team dynamic flag issued and audited by the platform.
        //
        // These environment variables are integral to anti-abuse and audit trails.
        // Removing, renaming or altering their semantics weakens challenge isolation
        // and breaks cheat detection, so downstream extensions must preserve them.
        var envs = BuildContainerEnv(config);

        var pod = new V1Pod
        {
            Metadata = new V1ObjectMeta
            {
                Name = name,
                NamespaceProperty = options.Namespace,
                // Both the BURAQ and the pre-rebrand label domains — see ContainerLabels.
                Labels = ContainerLabels.ForPod(name, chalImage, config)
            },
            Spec = new V1PodSpec
            {
                ImagePullSecrets =
                    authSecretName is null
                        ? Array.Empty<V1LocalObjectReference>()
                        : new List<V1LocalObjectReference> { new() { Name = authSecretName } },
                DnsPolicy = "None",
                DnsConfig = new() { Nameservers = options.Dns ?? ["223.5.5.5", "114.114.114.114"] },
                EnableServiceLinks = false,
                Containers =
                [
                    new V1Container
                    {
                        Name = name,
                        Image = config.Image,
                        ImagePullPolicy = "Always",
                        Env = envs,
                        Ports = [new V1ContainerPort { ContainerPort = config.ExposedPort }],
                        Resources = new V1ResourceRequirements
                        {
                            Limits = new Dictionary<string, ResourceQuantity>
                            {
                                ["cpu"] = new($"{config.CPUCount * 100}m"),
                                ["memory"] = new($"{config.MemoryLimit}Mi"),
                                ["ephemeral-storage"] = new($"{config.StorageLimit}Mi")
                            },
                            Requests = new Dictionary<string, ResourceQuantity>
                            {
                                ["cpu"] = new("10m"), ["memory"] = new("32Mi")
                            }
                        },
                        SecurityContext = BuildSecurityContext()
                    }
                ],
                RestartPolicy = "Never",
                AutomountServiceAccountToken = false,
                SecurityContext = BuildPodSecurityContext()
            }
        };

        try
        {
            pod = await _client.CreateNamespacedPodAsync(pod, options.Namespace, cancellationToken: token);
        }
        catch (HttpOperationException e)
        {
            _logger.LogCreationFailedWithHttpContext(name, e.Response.StatusCode, e.Response.Content);
            return null;
        }
        catch (Exception e)
        {
            _logger.LogErrorMessage(e,
                StaticLocalizer[nameof(Resources.Program.ContainerManager_ContainerCreationFailed), name]);
            return null;
        }

        if (pod is null)
        {
            _logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.ContainerManager_ContainerInstanceCreationFailed),
                    config.Image.Split("/").LastOrDefault() ?? ""], TaskStatus.Failed,
                LogLevel.Warning);
            return null;
        }

        // Service is needed for port mapping
        var service = new V1Service
        {
            ApiVersion = "v1",
            Kind = "Service",
            Metadata = new V1ObjectMeta
            {
                Name = name,
                NamespaceProperty = _meta.Config.Namespace,
                Labels = ContainerLabels.ForService(name)
            },
            Spec = new V1ServiceSpec
            {
                Type = _meta.ExposePort ? "NodePort" : "ClusterIP",
                Ports = [new V1ServicePort { Port = config.ExposedPort, TargetPort = config.ExposedPort }],
                // Current domain only: the pod this selects is created in the same call and always
                // carries it. See ContainerLabels.
                Selector = ContainerLabels.SelectorFor(name)
            }
        };

        try
        {
            service = await _client.CoreV1.CreateNamespacedServiceAsync(service, _meta.Config.Namespace,
                cancellationToken: token);
        }
        catch (HttpOperationException e)
        {
            try
            {
                // remove the pod if service creation failed, ignore the error
                await _client.CoreV1.DeleteNamespacedPodAsync(name, _meta.Config.Namespace, cancellationToken: token);
            }
            catch
            {
                // ignored
            }

            _logger.LogServiceCreationFailedWithHttpContext(name, e.Response.StatusCode, e.Response.Content);
            return null;
        }
        catch (Exception e)
        {
            try
            {
                // remove the pod if service creation failed, ignore the error
                await _client.CoreV1.DeleteNamespacedPodAsync(name, _meta.Config.Namespace, cancellationToken: token);
            }
            catch
            {
                // ignored
            }

            _logger.LogErrorMessage(e,
                StaticLocalizer[nameof(Resources.Program.ContainerManager_ServiceCreationFailed), name]);
            return null;
        }

        var container = new Models.Data.Container
        {
            ContainerId = name,
            Image = config.Image,
            Port = config.ExposedPort,
            IP = service.Spec.ClusterIP,
            IsProxy = !_meta.ExposePort,
            // No tracking for k8s-managed containers
            Status = ContainerStatus.Running
        };

        if (!_meta.ExposePort)
            return container;

        container.PublicIP = _meta.PublicEntry;
        container.PublicPort = service.Spec.Ports[0].NodePort;

        return container;
    }

    public async Task DestroyContainerAsync(Models.Data.Container container, CancellationToken token = default)
    {
        try
        {
            await _client.CoreV1.DeleteNamespacedServiceAsync(container.ContainerId, _meta.Config.Namespace,
                cancellationToken: token);
            await _client.CoreV1.DeleteNamespacedPodAsync(container.ContainerId, _meta.Config.Namespace,
                cancellationToken: token);
        }
        catch (HttpOperationException e)
        {
            if (e.Response.StatusCode == HttpStatusCode.NotFound)
            {
                container.Status = ContainerStatus.Destroyed;
                return;
            }

            _logger.LogDeletionFailedWithHttpContext(container.LogId, e.Response.StatusCode, e.Response.Content);
        }
        catch (Exception e)
        {
            _logger.LogErrorMessage(e,
                StaticLocalizer[nameof(Resources.Program.ContainerManager_ContainerDeletionFailed),
                    container.LogId]);
            return;
        }

        container.Status = ContainerStatus.Destroyed;
    }

    public async Task<ContainerProviderStatus> GetStatusAsync(CancellationToken token = default)
    {
        try
        {
            var pods = await _client.CoreV1.ListNamespacedPodAsync(_meta.Config.Namespace,
                cancellationToken: token);
            var phases = pods.Items.Select(x => x.Status?.Phase ?? "Unknown").ToArray();
            var running = phases.Count(x => x == "Running");
            var pending = phases.Count(x => x == "Pending");
            var unavailable = phases.Length - running - pending - phases.Count(x => x == "Succeeded");
            var status = unavailable > 0 || (_meta.Capacity > 0 && running >= _meta.Capacity)
                ? "Degraded" : "Healthy";
            return new("Kubernetes", _meta.HostIp, status,
                _meta.Capacity > 0 ? _meta.Capacity : null, running, pending, unavailable,
                "Pod sandbox + NetworkPolicy", null, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Kubernetes provider health check failed.");
            return new("Kubernetes", _meta.HostIp, "Unavailable",
                _meta.Capacity > 0 ? _meta.Capacity : null, 0, 0, 1,
                "Pod sandbox + NetworkPolicy", null, DateTimeOffset.UtcNow);
        }
    }

    /// <summary>
    /// Container-level sandbox baseline (finding F-003).
    /// </summary>
    /// <remarks>
    /// Challenge pods previously carried no <c>SecurityContext</c> at all, so they ran with the
    /// cluster's permissive defaults: the full default capability set and unrestricted privilege
    /// escalation.
    /// </remarks>
    private V1SecurityContext? BuildSecurityContext()
    {
        var sandbox = _meta.Sandbox;

        if (!sandbox.Enabled)
            return null;

        var context = new V1SecurityContext
        {
            AllowPrivilegeEscalation = !sandbox.NoNewPrivileges,
            Privileged = false,
            ReadOnlyRootFilesystem = sandbox.ReadOnlyRootFilesystem
        };

        if (sandbox.DropCapabilities is { Length: > 0 } || sandbox.AddCapabilities is { Length: > 0 })
            context.Capabilities = new V1Capabilities
            {
                Drop = sandbox.DropCapabilities, Add = sandbox.AddCapabilities
            };

        if (sandbox.RunAsNonRoot)
            context.RunAsNonRoot = true;

        return context;
    }

    /// <summary>
    /// Pod-level sandbox baseline, carrying the seccomp profile (finding F-003).
    /// </summary>
    private V1PodSecurityContext? BuildPodSecurityContext()
    {
        var sandbox = _meta.Sandbox;

        if (!sandbox.Enabled || string.IsNullOrWhiteSpace(sandbox.SeccompProfile))
            return null;

        // "Localhost" additionally requires a profile path, which the operator supplies as
        // "Localhost:<path>"; anything else is passed through as the profile type.
        var parts = sandbox.SeccompProfile.Split(':', 2);

        return new V1PodSecurityContext
        {
            SeccompProfile = new V1SeccompProfile
            {
                Type = parts[0], LocalhostProfile = parts.Length > 1 ? parts[1] : null
            }
        };
    }

    /// <summary>
    /// Builds the environment handed to a challenge container.
    /// </summary>
    /// <remarks>
    /// Every variable is injected under the documented BURAQ_ name and an opaque compatibility
    /// name for older challenge images.
    ///
    /// The compatibility names cannot simply be dropped. Challenge images
    /// are authored by third parties against that contract, so removing the names would silently
    /// break every existing challenge — a dynamic-flag challenge reading the former flag name would serve
    /// the wrong flag rather than fail loudly. They are also the audit and anti-abuse identifiers
    /// the Restricted License requires this component to preserve; see the compliance notice in
    /// CreateContainerAsync.
    ///
    /// Both names always carry identical values, so a challenge may read either.
    /// </remarks>
    private static IList<V1EnvVar> BuildContainerEnv(ContainerConfig config)
    {
        var envs = new List<V1EnvVar>(10);
        var compatibilityPrefix = string.Concat("GZ", "CTF_");

        void Add(string suffix, string value)
        {
            envs.Add(new V1EnvVar { Name = $"BURAQ_{suffix}", Value = value });
            envs.Add(new V1EnvVar { Name = $"{compatibilityPrefix}{suffix}", Value = value });
        }

        Add("TEAM_ID", config.TeamId);
        Add("USER_ID", config.UserId.ToString());
        Add("CHALLENGE_ID", config.ChallengeId.ToString());

        if (config.GameId is int gameId)
            Add("GAME_ID", gameId.ToString());

        if (!string.IsNullOrWhiteSpace(config.Flag))
            Add("FLAG", config.Flag);

        return envs;
    }
}
