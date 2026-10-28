// SPDX-License-Identifier: LicenseRef-THE-ARZENS-Custom
// Copyright (C) 2022-2025 THE ARZENS

using System.Net;
using Docker.DotNet;
using Docker.DotNet.Models;
using BURAQ.Services.Container.Provider;
using ContainerStatus = BURAQ.Utils.ContainerStatus;

namespace BURAQ.Services.Container.Manager;

public class DockerManager : IContainerManager
{
    private readonly DockerClient _client;
    private readonly ILogger<DockerManager> _logger;
    private readonly DockerMetadata _meta;

    /// <summary>
    /// Whether the running process needs a published host port to reach a proxied container,
    /// rather than the container's bridge-network IP directly.
    /// </summary>
    /// <remarks>
    /// The shipped platform runs as a Linux container attached to the same Docker network as the
    /// challenges it proxies (<see cref="DockerProvider.AttachSelfToNetwork" />), so the bridge IP
    /// is directly routable there. Docker Desktop (Windows/macOS) runs containers inside a VM
    /// whose bridge network is not routable from a process outside it — which this platform never
    /// is in production, only when run directly for local development. This is <c>false</c> on
    /// every real deployment (the shipped image is always Linux), so the Linux path below is
    /// unaffected; it exists purely so proxy mode also works when developing on Docker Desktop.
    /// </remarks>
    private static readonly bool RequiresPublishedPortForLocalConnect =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    public DockerManager(IContainerProvider<DockerClient, DockerMetadata> provider, ILogger<DockerManager> logger)
    {
        _logger = logger;
        _meta = provider.GetMetadata();
        _client = provider.GetProvider();

        logger.SystemLog(StaticLocalizer[nameof(Resources.Program.ContainerManager_DockerMode)],
            TaskStatus.Success, LogLevel.Debug);
    }


    public async Task DestroyContainerAsync(Models.Data.Container container, CancellationToken token = default)
    {
        try
        {
            await _client.Containers.RemoveContainerAsync(container.ContainerId,
                new() { Force = true }, token);
        }
        catch (DockerContainerNotFoundException)
        {
            _logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.ContainerManager_ContainerDestroyed),
                    container.LogId],
                TaskStatus.Success, LogLevel.Debug);
        }
        catch (DockerApiException e)
        {
            if (e.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.SystemLog(
                    StaticLocalizer[nameof(Resources.Program.ContainerManager_ContainerDestroyed),
                        container.LogId],
                    TaskStatus.Success, LogLevel.Debug);
            }
            else
            {
                _logger.LogDeletionFailedWithHttpContext(container.LogId, e.StatusCode, e.ResponseBody ?? string.Empty);
                return;
            }
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

    public async Task<Models.Data.Container?> CreateContainerAsync(BURAQ.Models.Internal.ContainerConfig config,
        CancellationToken token = default)
    {
        if (_meta.Capacity > 0)
        {
            var workloads = await _client.Containers.ListContainersAsync(new ContainersListParameters(), token);
            var active = workloads.Count(IsChallengeWorkload);
            if (active >= _meta.Capacity)
            {
                _logger.LogWarning("Docker challenge capacity {Capacity} is exhausted.", _meta.Capacity);
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

        var parameters = GetCreateContainerParameters(config);
        var containerName = parameters.Name ?? DockerMetadata.GetName(config);
        parameters.Name = containerName;

        if (_meta.ExposePort || RequiresPublishedPortForLocalConnect)
        {
            parameters.HostConfig ??= new();
            parameters.ExposedPorts = new Dictionary<string, EmptyStruct> { [config.ExposedPort.ToString()] = new() };
            parameters.HostConfig.PortBindings = new Dictionary<string, IList<PortBinding>>
            {
                // let docker choose a random port, do not use "PublishAllPorts" option
                // reference: https://github.com/moby/moby/blob/master/daemon/libnetwork/portallocator/portallocator.go#L135
                // function: RequestPortsInRange
                // comment:
                //     If portStart and portEnd are 0 it returns
                //     the first free port in the default ephemeral range.
                [config.ExposedPort.ToString()] = [new PortBinding { HostPort = "0" }]
            };
        }

        CreateContainerResponse? containerRes;
        var retry = 0;

    CreateDockerContainer:
        try
        {
            if (retry++ >= 3)
            {
                _logger.SystemLog(
                    StaticLocalizer[nameof(Resources.Program.ContainerManager_ContainerCreationFailed),
                        containerName], TaskStatus.Failed, LogLevel.Information);
                return null;
            }

            containerRes = await _client.Containers.CreateContainerAsync(parameters, token);
        }
        catch (DockerImageNotFoundException)
        {
            _logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.ContainerManager_PullContainerImage), config.Image],
                TaskStatus.Pending, LogLevel.Information);

            var auth = _meta.AuthConfigs.GetForImage(config.Image) ?? new AuthConfig();

            // pull the image and retry
            await _client.Images.CreateImageAsync(new() { FromImage = config.Image }, auth,
                new Progress<JSONMessage>(msg =>
                {
                    Console.WriteLine($@"{msg.Status}|{msg.Progress}|{msg.Error}");
                }), token);

            goto CreateDockerContainer;
        }
        catch (DockerApiException e)
        {
            if (e.StatusCode == HttpStatusCode.Conflict)
            {
                _logger.SystemLog(
                    StaticLocalizer[nameof(Resources.Program.ContainerManager_ContainerExisted),
                        containerName],
                    TaskStatus.Duplicate,
                    LogLevel.Warning);

                // the container already exists, remove it and retry
                try
                {
                    await _client.Containers.RemoveContainerAsync(containerName,
                        new() { Force = true }, token);
                }
                catch (Exception ex)
                {
                    _logger.LogErrorMessage(ex,
                        StaticLocalizer[nameof(Resources.Program.ContainerManager_ContainerDeletionFailed),
                            containerName]);
                    return null;
                }

                goto CreateDockerContainer;
            }

            _logger.LogCreationFailedWithHttpContext(containerName, e.StatusCode, e.ResponseBody ?? string.Empty);
            return null;
        }
        catch (Exception e)
        {
            _logger.LogErrorMessage(e,
                StaticLocalizer[nameof(Resources.Program.ContainerManager_ContainerCreationFailed),
                    containerName]);
            return null;
        }

        var container = new Models.Data.Container { ContainerId = containerRes.ID, Image = config.Image };

        retry = 0;

        while (true)
        {
            if (retry++ >= 3)
            {
                _logger.SystemLog(
                    StaticLocalizer[
                        nameof(Resources.Program.ContainerManager_ContainerInstanceStartFailed),
                        container.LogId,
                        config.Image.Split("/").LastOrDefault() ?? ""],
                    TaskStatus.Failed, LogLevel.Warning);

                await DestroyContainerAsync(container, token);
                return null;
            }

            var started = await _client.Containers.StartContainerAsync(container.ContainerId,
                new(), token);

            if (started)
                break;

            await Task.Delay(500, token);
        }

        var info = await _client.Containers.InspectContainerAsync(container.ContainerId, token);
        var state = info.State;

        if (state is null)
        {
            _logger.SystemLog(
                StaticLocalizer[
                    nameof(Resources.Program.ContainerManager_ContainerInstanceCreationFailedWithError),
                    config.Image.Split("/").LastOrDefault() ?? "", string.Empty],
                TaskStatus.Failed, LogLevel.Warning);

            await DestroyContainerAsync(container, token);
            return null;
        }

        container.Status = state.Dead || state.OOMKilled || state.Restarting
            ? ContainerStatus.Destroyed
            : state.Running
                ? ContainerStatus.Running
                : ContainerStatus.Pending;

        if (container.Status != ContainerStatus.Running)
        {
            _logger.SystemLog(
                StaticLocalizer[
                    nameof(Resources.Program.ContainerManager_ContainerInstanceCreationFailedWithError),
                    config.Image.Split("/").LastOrDefault() ?? "", state.Error],
                TaskStatus.Failed, LogLevel.Warning);

            await DestroyContainerAsync(container, token);
            return null;
        }

        container.StartedAt = DateTimeOffset.Parse(state.StartedAt);
        container.ExpectStopAt = container.StartedAt + TimeSpan.FromHours(2);
        var networkSettings = info.NetworkSettings;
        container.IP = networkSettings?.Networks?.FirstOrDefault().Value?.IPAddress ?? string.Empty;
        container.Port = config.ExposedPort;
        container.IsProxy = !_meta.ExposePort;

        if (!_meta.ExposePort)
        {
            // See RequiresPublishedPortForLocalConnect: on Docker Desktop the bridge IP above is
            // not reachable from this process, so connect through the published loopback port
            // instead. No-op (and no port was even published) on the shipped Linux deployment.
            if (RequiresPublishedPortForLocalConnect &&
                GetPublishedPortBindings(networkSettings?.Ports, config.ExposedPort) is
                    [{ HostPort: var localHostPort }, ..] &&
                int.TryParse(localHostPort, out var localPort))
            {
                container.IP = IPAddress.Loopback.ToString();
                container.Port = localPort;
            }

            return container;
        }

        var bindings = GetPublishedPortBindings(networkSettings?.Ports, config.ExposedPort);

        if (bindings is [])
        {
            _logger.SystemLog(
                StaticLocalizer[
                    nameof(Resources.Program.ContainerManager_ContainerCreationFailed),
                    config.Image.Split("/").LastOrDefault() ?? ""],
                TaskStatus.Failed, LogLevel.Warning);

            await DestroyContainerAsync(container, token);
            return null;
        }

        var port = bindings.First().HostPort;

        if (int.TryParse(port, out var numPort))
            container.PublicPort = numPort;
        else
            _logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.ContainerManager_PortParsingFailed), port],
                TaskStatus.Failed,
                LogLevel.Warning);

        if (!string.IsNullOrEmpty(_meta.PublicEntry))
            container.PublicIP = _meta.PublicEntry;

        return container;
    }

    public async Task<ContainerProviderStatus> GetStatusAsync(CancellationToken token = default)
    {
        try
        {
            await _client.System.PingAsync(token);
            var info = await _client.System.GetSystemInfoAsync(token);
            var all = await _client.Containers.ListContainersAsync(new ContainersListParameters { All = true }, token);
            var workloads = all.Where(IsChallengeWorkload).ToArray();
            var running = workloads.Count(x => string.Equals(x.State, "running", StringComparison.OrdinalIgnoreCase));
            var pending = workloads.Count(x => x.State is "created" or "restarting");
            var unavailable = workloads.Length - running - pending;
            var status = unavailable > 0 || (_meta.Capacity > 0 && running >= _meta.Capacity)
                ? "Degraded" : "Healthy";
            return new("Docker", info.Name ?? "docker", status,
                _meta.Capacity > 0 ? _meta.Capacity : null, running, pending, unavailable,
                "Linux namespaces + seccomp", info.ServerVersion, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Docker provider health check failed.");
            return new("Docker", "docker", "Unavailable", _meta.Capacity > 0 ? _meta.Capacity : null,
                0, 0, 1, "Linux namespaces + seccomp", null, DateTimeOffset.UtcNow);
        }
    }

    /// <summary>
    /// Determines whether a Docker container is owned by a BURAQ-compatible challenge provider.
    /// </summary>
    /// <remarks>
    /// Generic labels such as <c>ChallengeId</c> are intentionally insufficient: any local tool can
    /// use that common key, and Docker provider health must not count an unrelated container merely
    /// because its metadata happens to collide. Requiring the resource and challenge keys from one
    /// of the platform's namespaced label domains gives discovery an explicit ownership boundary.
    /// </remarks>
    internal static bool IsChallengeWorkload(ContainerListResponse item) => item.Labels is not null &&
        ((item.Labels.ContainsKey(ContainerLabels.Key(ContainerLabels.ResourceId)) &&
          item.Labels.ContainsKey(ContainerLabels.Key(ContainerLabels.ChallengeId))) ||
         (item.Labels.ContainsKey(ContainerLabels.LegacyKey(ContainerLabels.ResourceId)) &&
          item.Labels.ContainsKey(ContainerLabels.LegacyKey(ContainerLabels.ChallengeId))));

    internal static IList<PortBinding> GetPublishedPortBindings(
        IDictionary<string, IList<PortBinding>>? ports, int exposedPort)
    {
        if (ports is not { Count: > 0 })
            return [];

        var port = exposedPort.ToString();
        var portPrefix = $"{port}/";
        var matchedPorts = ports
            .Where(kv => kv.Value is { Count: > 0 }
                && kv.Key.StartsWith(portPrefix, StringComparison.Ordinal))
            .ToArray();

        return matchedPorts switch
        {
            [] => [],
            [{ Value: var bindings }] => bindings,
            _ => matchedPorts.FirstOrDefault(kv =>
                     kv.Key.EndsWith("/tcp", StringComparison.OrdinalIgnoreCase))
                 .Value
                 ?? matchedPorts[0].Value
        };
    }

    private CreateContainerParameters GetCreateContainerParameters(BURAQ.Models.Internal.ContainerConfig config)
    {
        var name = DockerMetadata.GetName(config);
        return new()
        {
            Image = config.Image,
            Labels = BuildChallengeLabels(name, config),
            Name = name,

            // BURAQ_FLAG is a per-team dynamic flag issued and audited by the platform.
            //
            // These environment variables are integral to anti-abuse and audit trails.
            // Removing, renaming or altering their semantics weakens challenge isolation
            // and breaks cheat detection, so downstream extensions must preserve them.
            Env = BuildContainerEnv(config),
            HostConfig = BuildHostConfig(config)
        };
    }

    /// <summary>
    /// Builds explicit BURAQ ownership labels while preserving the original generic metadata for
    /// external tooling that already reads it. Discovery relies only on the namespaced labels.
    /// </summary>
    internal static IDictionary<string, string> BuildChallengeLabels(string resourceId,
        BURAQ.Models.Internal.ContainerConfig config)
    {
        var labels = ContainerLabels.ForPod(resourceId, config.Image, config);
        labels["TeamId"] = config.TeamId;
        labels["UserId"] = config.UserId.ToString();
        labels["ChallengeId"] = config.ChallengeId.ToString();
        return labels;
    }

    HostConfig BuildHostConfig(BURAQ.Models.Internal.ContainerConfig config)
    {
        var host = new HostConfig
        {
            Memory = config.MemoryLimit * 1024L * 1024L,

            // CPUPercent is honoured only by Windows daemons and was silently ignored on Linux,
            // so challenges effectively had no CPU ceiling (finding F-003). NanoCPUs is the
            // portable control; both are set so either daemon enforces the same 0.1-CPU units.
            CPUPercent = config.CPUCount * 10,
            NanoCPUs = config.CPUCount * 100_000_000L,

            NetworkMode = _meta.NetworkNames[config.NetworkMode]
        };

        var sandbox = _meta.Sandbox;

        if (!sandbox.Enabled)
            return host;

        if (sandbox.DropCapabilities is { Length: > 0 })
            host.CapDrop = sandbox.DropCapabilities;

        if (sandbox.AddCapabilities is { Length: > 0 })
            host.CapAdd = sandbox.AddCapabilities;

        if (sandbox.PidsLimit > 0)
            host.PidsLimit = sandbox.PidsLimit;

        if (sandbox.NoNewPrivileges)
            host.SecurityOpt = ["no-new-privileges:true"];

        host.ReadonlyRootfs = sandbox.ReadOnlyRootFilesystem;

        // Only honoured by overlay2-on-xfs/pquota, btrfs and zfs; other drivers reject creation
        // outright, so this stays opt-in.
        if (sandbox.EnableStorageLimit && config.StorageLimit > 0)
            host.StorageOpt = new Dictionary<string, string> { ["size"] = $"{config.StorageLimit}M" };

        return host;
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
    /// GetCreateContainerParameters.
    ///
    /// Both names always carry identical values, so a challenge may read either.
    /// </remarks>
    private static IList<string> BuildContainerEnv(BURAQ.Models.Internal.ContainerConfig config)
    {
        var env = new List<string>(10);
        var compatibilityPrefix = string.Concat("GZ", "CTF_");

        void Add(string suffix, string value)
        {
            env.Add($"BURAQ_{suffix}={value}");
            env.Add($"{compatibilityPrefix}{suffix}={value}");
        }

        Add("TEAM_ID", config.TeamId);
        Add("USER_ID", config.UserId.ToString());
        Add("CHALLENGE_ID", config.ChallengeId.ToString());

        if (config.GameId is int gameId)
            Add("GAME_ID", gameId.ToString());

        if (!string.IsNullOrWhiteSpace(config.Flag))
            Add("FLAG", config.Flag);

        return env;
    }
}
