using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using BURAQ.Hubs;
using BURAQ.Hubs.Clients;
using BURAQ.Models.Request.Admin;
using BURAQ.Services.Container.Provider;
using BURAQ.Services.Container.Manager;
using BURAQ.Utils;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BURAQ.Services.Soc;

/// <summary>Collects one bounded server-side snapshot; browsers never query Docker directly.</summary>
public sealed class ResourceMonitoringService(
    IServiceScopeFactory scopeFactory,
    IServiceProvider services,
    IHubContext<AdminHub, IAdminClient> hub,
    ILogger<ResourceMonitoringService> logger) : BackgroundService
{
    private const int SampleSeconds = 10;
    private const int MaxHistoryPoints = 7 * 24 * 60 * 6;
    private static readonly AppJsonSerializerContext SocJsonContext =
        new(new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private readonly ConcurrentDictionary<Guid, SocContainerMetric> _containers = new();
    private readonly ConcurrentQueue<SocMetricPoint> _history = new();
    private SocHostSnapshot _host = EmptyHost();
    private ContainerProviderStatus _provider = EmptyProvider();
    private ulong _previousIdle;
    private ulong _previousTotal;

    // S2 — container egress: a challenge container pushing data out fast is exfiltration, a
    // scan, or a box being used to attack something else. Rates are computed between samples.
    private const double EgressAlertBytesPerSecond = 2 * 1024 * 1024;
    private readonly ConcurrentDictionary<Guid, (long Tx, DateTimeOffset At)> _lastTx = new();
    private readonly ConcurrentDictionary<Guid, double> _egressSpikes = new();

    public SocHostSnapshot Host => _host;
    public ContainerProviderStatus Provider => _provider;
    public SocContainerMetric[] ContainerMetrics => _containers.Values.ToArray();
    public SocMetricPoint[] HistorySince(DateTimeOffset since) => _history.Where(x => x.Time >= since).ToArray();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await SampleAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(SampleSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await SampleAsync(stoppingToken);
    }

    private async Task SampleAsync(CancellationToken token)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var alerts = scope.ServiceProvider.GetRequiredService<ISocAlertService>();
            var health = scope.ServiceProvider.GetRequiredService<HealthCheckService>();
            var healthReport = await health.CheckHealthAsync(token);
            var serviceStates = healthReport.Entries.ToDictionary(x => x.Key, x => x.Value.Status.ToString());
            serviceStates["BURAQ API"] = "Healthy";
            serviceStates["SignalR"] = "Healthy";

            var runtime = services.GetRequiredService<IContainerManager>();
            try
            {
                _provider = await runtime.GetStatusAsync(token);
            }
            catch
            {
                _provider = EmptyProvider();
            }
            serviceStates[$"{_provider.Provider} runtime"] = _provider.Status;

            var docker = services.GetService<IContainerProvider<DockerClient, DockerMetadata>>()?.GetProvider();

            var open = await db.Containers.AsNoTracking()
                .Where(c => c.GameInstanceId != null && c.Status != BURAQ.Utils.ContainerStatus.Destroyed)
                .Select(c => new
                {
                    c.Id, c.ContainerId,
                    TeamId = (int?)c.GameInstance!.Participation.TeamId,
                    GameId = (int?)c.GameInstance.Participation.GameId,
                    ChallengeId = (int?)c.GameInstance.ChallengeId
                })
                .ToArrayAsync(token);

            if (docker is not null)
                await CollectContainersAsync(docker, open.Select(x => (x.Id, x.ContainerId)).ToArray(), token);

            foreach (var stale in _containers.Keys.Except(open.Select(x => x.Id)).ToArray())
                _containers.TryRemove(stale, out _);
            foreach (var stale in _lastTx.Keys.Except(open.Select(x => x.Id)).ToArray())
                _lastTx.TryRemove(stale, out _);

            foreach (var (containerGuid, rate) in _egressSpikes.ToArray())
            {
                var info = open.FirstOrDefault(x => x.Id == containerGuid);
                var bucket = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 600; // one alert per container per 10 min
                await alerts.CreateAsync(new SocAlert
                {
                    SourceKey = $"egress:{containerGuid:N}:{bucket}",
                    Event = "CONTAINER EGRESS SPIKE",
                    Description = $"Challenge container {(info?.ContainerId is { Length: > 12 } cid ? cid[..12] : info?.ContainerId)} " +
                                  $"sent {rate / (1024 * 1024):F1} MB/s out. Possible exfiltration, scanning or abuse of the box.",
                    Category = SocCategory.Infrastructure,
                    Severity = SocSeverity.Medium,
                    RiskImpact = 10,
                    TeamId = info?.TeamId,
                    GameId = info?.GameId,
                    ChallengeId = info?.ChallengeId,
                    InstanceId = containerGuid
                }, token);
            }
            _egressSpikes.Clear();

            var (cpu, load) = ReadCpu();
            var (memoryUsed, memoryTotal) = ReadMemory();
            var (storageUsed, storageTotal) = ReadStorage();
            var memoryPercent = Percent(memoryUsed, memoryTotal);
            var storagePercent = Percent(storageUsed, storageTotal);
            var status = serviceStates.Values.Any(x => x is "Unhealthy" or "Critical") ? "Degraded" : "Healthy";

            _host = new("Primary Challenge Host", status, DateTimeOffset.UtcNow,
                Environment.ProcessorCount, cpu, load, memoryUsed, memoryTotal, memoryPercent,
                storageUsed, storageTotal, storagePercent, serviceStates);

            var point = new SocMetricPoint(_host.SampledAt, cpu ?? 0, memoryPercent ?? 0, storagePercent ?? 0,
                open.Count(x => _containers.GetValueOrDefault(x.Id)?.RuntimeStatus == "Running"));
            _history.Enqueue(point);
            while (_history.Count > MaxHistoryPoints) _history.TryDequeue(out _);

            await CreateThresholdAlertAsync(alerts, "cpu", cpu, 90, "HIGH CPU USAGE", token);
            await CreateThresholdAlertAsync(alerts, "memory", memoryPercent, 90, "HIGH MEMORY USAGE", token);
            await CreateThresholdAlertAsync(alerts, "storage", storagePercent, 90, "STORAGE NEAR CAPACITY", token);

            await hub.Clients.All.ReceivedSocUpdate(new("snapshot", _host.SampledAt,
                JsonSerializer.SerializeToElement(new SocResourceSnapshot(_host, point, ContainerMetrics),
                    SocJsonContext.SocResourceSnapshot)));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { logger.LogWarning(ex, "SOC resource sampling failed."); }
    }

    private async Task CollectContainersAsync(DockerClient docker, (Guid Id, string RuntimeId)[] containers,
        CancellationToken token)
    {
        using var gate = new SemaphoreSlim(8);
        await Task.WhenAll(containers.Select(async item =>
        {
            await gate.WaitAsync(token);
            try
            {
                ContainerStatsResponse? value = null;
                await docker.Containers.GetContainerStatsAsync(item.RuntimeId,
                    new ContainerStatsParameters { Stream = false },
                    new InlineProgress<ContainerStatsResponse>(x => value = x), token);
                if (value is null) return;

                var currentCpu = value.CPUStats?.CPUUsage?.TotalUsage ?? 0;
                var previousCpu = value.PreCPUStats?.CPUUsage?.TotalUsage ?? 0;
                var currentSystem = value.CPUStats?.SystemUsage ?? 0;
                var previousSystem = value.PreCPUStats?.SystemUsage ?? 0;
                var cpuDelta = currentCpu >= previousCpu ? currentCpu - previousCpu : 0;
                var systemDelta = currentSystem >= previousSystem ? currentSystem - previousSystem : 0;
                var onlineCpus = value.CPUStats?.OnlineCPUs ?? 0;
                var cpuCount = onlineCpus > 0 ? onlineCpus : (double)Environment.ProcessorCount;
                var cpu = systemDelta > 0 ? cpuDelta * cpuCount * 100d / systemDelta : 0;
                var rx = value.Networks?.Values.Sum(x => (long)x.RxBytes) ?? 0;
                var tx = value.Networks?.Values.Sum(x => (long)x.TxBytes) ?? 0;

                var sampledAt = DateTimeOffset.UtcNow;
                if (_lastTx.TryGetValue(item.Id, out var previous) && tx >= previous.Tx)
                {
                    var seconds = (sampledAt - previous.At).TotalSeconds;
                    if (seconds >= 1 && (tx - previous.Tx) / seconds > EgressAlertBytesPerSecond)
                        _egressSpikes[item.Id] = (tx - previous.Tx) / seconds;
                }
                _lastTx[item.Id] = (tx, sampledAt);
                _containers[item.Id] = new(item.Id, Math.Round(cpu, 2), (long)(value.MemoryStats?.Usage ?? 0),
                    (long)(value.MemoryStats?.Limit ?? 0), rx, tx, 0, "Running");
            }
            catch { _containers[item.Id] = new(item.Id, null, null, null, 0, 0, 0, "Unavailable"); }
            finally { gate.Release(); }
        }));
    }

    private static async Task CreateThresholdAlertAsync(ISocAlertService alerts, string key,
        double? value, double threshold, string eventName, CancellationToken token)
    {
        if (value is null || value < threshold)
            return;

        await alerts.CreateAsync(new SocAlert
        {
            SourceKey = $"infra:{key}:{DateTimeOffset.UtcNow:yyyyMMddHH}",
            Event = eventName,
            Description = $"BURAQ application node reported {value:F1}% {key} usage.",
            Category = SocCategory.Infrastructure,
            Severity = SocSeverity.High,
            RiskImpact = 20
        }, token);
    }

    private (double? Cpu, double? Load) ReadCpu()
    {
        if (OperatingSystem.IsLinux() && File.Exists("/proc/stat"))
        {
            var values = File.ReadLines("/proc/stat").First().Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Skip(1).Select(ulong.Parse).ToArray();
            var idle = values[3] + (values.Length > 4 ? values[4] : 0);
            var total = values.Aggregate(0UL, (sum, value) => sum + value);
            var deltaTotal = total - _previousTotal;
            double? cpu = _previousTotal == 0 || deltaTotal == 0 ? null :
                Math.Round((deltaTotal - (idle - _previousIdle)) * 100d / deltaTotal, 2);
            _previousIdle = idle; _previousTotal = total;
            var loadText = File.Exists("/proc/loadavg") ? File.ReadAllText("/proc/loadavg").Split(' ')[0] : null;
            double? load = double.TryParse(loadText, System.Globalization.CultureInfo.InvariantCulture, out var l) ? l : null;
            return (cpu, load);
        }
        return (null, null);
    }

    private static (long? Used, long? Total) ReadMemory()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/proc/meminfo")) return (null, null);
        var values = File.ReadLines("/proc/meminfo").Select(x => x.Split(':', 2))
            .Where(x => x.Length == 2).ToDictionary(x => x[0], x => long.Parse(x[1].Trim().Split(' ')[0]) * 1024);
        if (!values.TryGetValue("MemTotal", out var total)) return (null, null);
        values.TryGetValue("MemAvailable", out var available);
        return (total - available, total);
    }

    private static (long? Used, long? Total) ReadStorage()
    {
        try
        {
            var full = Path.GetFullPath(AppContext.BaseDirectory);
            var drive = DriveInfo.GetDrives().Where(x => x.IsReady && full.StartsWith(x.RootDirectory.FullName,
                StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => x.RootDirectory.FullName.Length).First();
            return (drive.TotalSize - drive.AvailableFreeSpace, drive.TotalSize);
        }
        catch { return (null, null); }
    }

    private static double? Percent(long? used, long? total) => used is null || total is null || total == 0
        ? null : Math.Round(used.Value * 100d / total.Value, 2);
    private static SocHostSnapshot EmptyHost() => new("Primary Challenge Host", "Starting", DateTimeOffset.UtcNow,
        Environment.ProcessorCount, null, null, null, null, null, null, null, null,
        new Dictionary<string, string>());
    private static ContainerProviderStatus EmptyProvider() => new("Runtime", "unavailable", "Starting",
        null, 0, 0, 0, "Unknown", null, DateTimeOffset.UtcNow);
    private sealed class InlineProgress<T>(Action<T> handler) : IProgress<T> { public void Report(T value) => handler(value); }
}
