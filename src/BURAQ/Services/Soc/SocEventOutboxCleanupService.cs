using Microsoft.EntityFrameworkCore;

namespace BURAQ.Services.Soc;

/// <summary>
/// Removes only successfully processed outbox delivery records after a bounded retention window.
/// Timeline events remain in SocEvents; pending, retrying and dead-lettered rows are never removed.
/// </summary>
public sealed class SocEventOutboxCleanupService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<SocEventOutboxCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private readonly int _retentionDays = Math.Clamp(
        configuration.GetValue<int?>("Soc:OutboxRetentionDays") ?? 7,
        1,
        90);

    // S4 — timeline retention. 0 (default) keeps every SOC event; set Soc:EventRetentionDays to
    // prune old, already-sealed rows (the ledger verifier then starts from the kept anchor seal).
    private readonly int _eventRetentionDays = Math.Clamp(
        configuration.GetValue<int?>("Soc:EventRetentionDays") ?? 0,
        0,
        3650);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "SOC outbox retention cleanup failed.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task CleanupAsync(CancellationToken token)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-_retentionDays);
        var removed = await db.SocEventOutbox
            .Where(x => x.ProcessedAtUtc != null && x.ProcessedAtUtc < cutoff)
            .ExecuteDeleteAsync(token);

        if (removed > 0)
            logger.LogInformation(
                "Removed {Count} processed SOC outbox records older than {RetentionDays} days.",
                removed,
                _retentionDays);

        if (_eventRetentionDays <= 0)
            return;

        // Prune whole sealed ranges only, up to the newest seal older than the cutoff, and keep
        // that seal as the chain's anchor so the ledger stays verifiable from there on.
        var eventCutoff = DateTimeOffset.UtcNow.AddDays(-_eventRetentionDays);
        var anchor = await db.SocEvents.AsNoTracking()
            .Where(x => x.EventType == SocLedger.SealEventType && x.TimestampUtc < eventCutoff)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(token);
        if (anchor is null || SocLedger.ReadSeal(anchor) is not { } sealedRange)
            return;

        var anchorId = anchor.Id;
        var lastSealed = sealedRange.ToId;
        var pruned = await db.SocEvents
            .Where(x => (x.EventType != SocLedger.SealEventType && x.Id <= lastSealed) ||
                        (x.EventType == SocLedger.SealEventType && x.Id < anchorId))
            .ExecuteDeleteAsync(token);
        if (pruned > 0)
            logger.LogInformation("Pruned {Count} SOC timeline events older than {Days} days.", pruned,
                _eventRetentionDays);
    }
}
