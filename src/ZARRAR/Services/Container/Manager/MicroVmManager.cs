using System.Net;
using ZARRAR.Models.Internal;
using ZARRAR.Services.Container.Provider;

namespace ZARRAR.Services.Container.Manager;

/// <summary>Runs challenge OCI images inside hardware-isolated MicroVMs through a trusted agent.</summary>
public sealed class MicroVmManager : IContainerManager
{
    private readonly MicroVmClient _client;
    private readonly MicroVmMetadata _metadata;
    private readonly ILogger<MicroVmManager> _logger;

    public MicroVmManager(IContainerProvider<MicroVmClient, MicroVmMetadata> provider,
        ILogger<MicroVmManager> logger)
    {
        _client = provider.GetProvider();
        _metadata = provider.GetMetadata();
        _logger = logger;
    }

    public async Task<Models.Data.Container?> CreateContainerAsync(ContainerConfig config,
        CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(config.Image) || config.ExposedPort is < 1 or > 65535 ||
            config.CPUCount <= 0 || config.MemoryLimit <= 0 || config.StorageLimit <= 0)
            return null;

        if (_metadata.Capacity > 0)
        {
            try
            {
                var health = await _client.HealthAsync(token);
                if (!string.Equals(health.Status, "Healthy", StringComparison.OrdinalIgnoreCase) ||
                    (long)Math.Max(0, health.Running) + Math.Max(0, health.Pending) >= _metadata.Capacity)
                {
                    _logger.LogWarning("MicroVM challenge capacity {Capacity} is unavailable.",
                        _metadata.Capacity);
                    return null;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "MicroVM capacity check failed closed.");
                return null;
            }
        }

        var workloadId = Guid.CreateVersion7();
        var environment = BuildIdentityEnvironment(config);
        var secrets = BuildSecretEnvironment(config);
        MicroVmCreateResponse response;
        try
        {
            response = await _client.CreateAsync(new MicroVmCreateRequest(
                workloadId, config.Image, config.ExposedPort, config.CPUCount,
                config.MemoryLimit, config.StorageLimit, config.NetworkMode.ToString(),
                config.EnableTrafficCapture, environment, secrets), token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "MicroVM control plane rejected workload {WorkloadId}.", workloadId);
            return null;
        }

        if (response.WorkloadId != workloadId ||
            !string.Equals(response.State, "Running", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(response.RuntimeId) || response.RuntimeId.Length > 200 ||
            !ValidGuestAddress(response.PrivateAddress) || response.PrivatePort is < 1 or > 65535 ||
            response.StartedAtUtc == default || response.ExpiresAtUtc <= response.StartedAtUtc)
        {
            _logger.LogWarning("MicroVM control plane returned an invalid create response for {WorkloadId}.",
                workloadId);
            await CleanupRejectedResponse(response, workloadId, token);
            return null;
        }

        if (_metadata.ExposePort && (string.IsNullOrWhiteSpace(response.PublicAddress) ||
                                    response.PublicPort is null or < 1 or > 65535))
        {
            await CleanupRejectedResponse(response, workloadId, token);
            return null;
        }

        return new Models.Data.Container
        {
            Id = workloadId,
            ContainerId = response.RuntimeId,
            Image = config.Image,
            Status = ContainerStatus.Running,
            StartedAt = response.StartedAtUtc,
            ExpectStopAt = response.ExpiresAtUtc,
            IP = response.PrivateAddress,
            Port = response.PrivatePort,
            PublicIP = response.PublicAddress,
            PublicPort = response.PublicPort,
            IsProxy = !_metadata.ExposePort
        };
    }

    public async Task DestroyContainerAsync(Models.Data.Container container,
        CancellationToken token = default)
    {
        try
        {
            await _client.DeleteAsync(container.ContainerId, container.Id, token);
            container.Status = ContainerStatus.Destroyed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "MicroVM control plane failed to delete workload {WorkloadId}.", container.Id);
        }
    }

    public async Task<ContainerProviderStatus> GetStatusAsync(CancellationToken token = default)
    {
        try
        {
            var health = await _client.HealthAsync(token);
            var capacity = health.Capacity > 0 ? health.Capacity : _metadata.Capacity;
            var status = string.Equals(health.Status, "Healthy", StringComparison.OrdinalIgnoreCase)
                ? "Healthy"
                : string.Equals(health.Status, "Degraded", StringComparison.OrdinalIgnoreCase)
                    ? "Degraded"
                    : "Unavailable";
            return new("MicroVM", string.IsNullOrWhiteSpace(health.Node) ? _metadata.Config.NodeName : health.Node,
                status, capacity > 0 ? capacity : null, Math.Max(0, health.Running),
                Math.Max(0, health.Pending), Math.Max(0, health.Unavailable),
                string.IsNullOrWhiteSpace(health.Isolation) ? "Hardware-isolated MicroVM" : health.Isolation,
                health.Version, DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "MicroVM provider health check failed.");
            return new("MicroVM", _metadata.Config.NodeName, "Unavailable",
                _metadata.Capacity > 0 ? _metadata.Capacity : null, 0, 0, 1,
                "Hardware-isolated MicroVM", null, DateTimeOffset.UtcNow);
        }
    }

    private static Dictionary<string, string> BuildIdentityEnvironment(ContainerConfig config)
    {
        var values = new Dictionary<string, string>();
        AddCompatible(values, "TEAM_ID", config.TeamId);
        AddCompatible(values, "USER_ID", config.UserId.ToString());
        AddCompatible(values, "CHALLENGE_ID", config.ChallengeId.ToString());
        if (config.GameId is int gameId) AddCompatible(values, "GAME_ID", gameId.ToString());
        return values;
    }

    private static Dictionary<string, string> BuildSecretEnvironment(ContainerConfig config)
    {
        var values = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(config.Flag)) AddCompatible(values, "FLAG", config.Flag);
        return values;
    }

    private static void AddCompatible(Dictionary<string, string> target, string name, string value)
    {
        target[$"ZARRAR_{name}"] = value;
        target[$"{string.Concat("GZ", "CTF_")}{name}"] = value;
    }

    private static bool ValidGuestAddress(string value) => IPAddress.TryParse(value, out var address) &&
        !IPAddress.IsLoopback(address) && !address.Equals(IPAddress.Any) &&
        !address.Equals(IPAddress.IPv6Any) && !address.IsIPv6Multicast;

    private async Task CleanupRejectedResponse(MicroVmCreateResponse response, Guid workloadId,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(response.RuntimeId) || response.RuntimeId.Length > 200)
            return;
        try { await _client.DeleteAsync(response.RuntimeId, workloadId, token); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to clean up rejected MicroVM response for {WorkloadId}.", workloadId);
        }
    }
}
