using Microsoft.EntityFrameworkCore;

namespace BURAQ.Services.Soc;

/// <summary>
/// Keeps the stored risk of active cases current as evidence ages (see
/// <see cref="SocRiskScoring.HalfLife" />), so the case list sorts by today's risk, not by the
/// peak it once had. Also applies the SOC configuration at startup.
/// </summary>
public sealed class SocRiskMaintenanceService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<SocRiskMaintenanceService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private const int BatchSize = 200;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        SocRiskScoring.Configure(configuration);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken);
                if (SocRiskScoring.HalfLife > TimeSpan.Zero)
                    await RefreshAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "SOC risk refresh failed.");
            }
        }
    }

    private async Task RefreshAsync(CancellationToken token)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var since = DateTimeOffset.UtcNow.AddDays(-14);

        // Only cases still on someone's desk; closed ones keep the score they were closed at.
        var cases = await db.SocRiskCases
            .Include(x => x.Evidence)
            .Where(x => (x.Status == SocAlertStatus.Open || x.Status == SocAlertStatus.Investigating ||
                         x.Status == SocAlertStatus.Escalated) && x.LastSeenAtUtc >= since)
            .OrderByDescending(x => x.LastSeenAtUtc)
            .Take(BatchSize)
            .ToArrayAsync(token);

        var changed = 0;
        var now = DateTimeOffset.UtcNow;
        foreach (var item in cases)
        {
            var score = SocRiskScoring.CalculateAt(item.Evidence.Select(x =>
                (x.Weight, x.Confidence, x.Kind, x.OccurredAtUtc)), now);
            if (score.Risk == item.RiskScore)
                continue;

            // Not UpdatedAtUtc: that marks analyst or evidence activity, not the clock.
            item.RiskScore = score.Risk;
            changed++;
        }

        if (changed > 0)
            await db.SaveChangesAsync(token);
    }
}
