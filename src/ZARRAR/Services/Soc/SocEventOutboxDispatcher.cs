using ZARRAR.Hubs;
using ZARRAR.Hubs.Clients;
using ZARRAR.Models.Request.Admin;
using ZARRAR.Utils;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ZARRAR.Services.Soc;

/// <summary>
/// Materializes committed outbox records into the existing SocEvents timeline. PostgreSQL
/// SKIP LOCKED allows multiple ZARRAR application nodes to dispatch safely without duplicates.
/// </summary>
public sealed class SocEventOutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IHubContext<AdminHub, IAdminClient> hub,
    ILogger<SocEventOutboxDispatcher> logger) : BackgroundService
{
    private const int BatchSize = 100;
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await DispatchBatchAsync(stoppingToken);
                if (processed == 0)
                    await Task.Delay(IdleDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "SOC event outbox dispatch failed.");
                await Task.Delay(IdleDelay, stoppingToken);
            }
        }
    }

    private async Task<int> DispatchBatchAsync(CancellationToken token)
    {
        SocEventOutboxEntry[] batch = [];

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(token);

            batch = await db.SocEventOutbox
                .FromSqlRaw($$"""
                    SELECT *
                    FROM "SocEventOutbox"
                    WHERE "ProcessedAtUtc" IS NULL
                      AND "AttemptCount" < 10
                      AND ("NextAttemptAtUtc" IS NULL OR "NextAttemptAtUtc" <= NOW())
                    ORDER BY "Id"
                    FOR UPDATE SKIP LOCKED
                    LIMIT {{BatchSize}}
                    """)
                .AsTracking()
                .ToArrayAsync(token);

            if (batch.Length == 0)
            {
                await transaction.CommitAsync(token);
                return 0;
            }

            var ids = batch.Select(x => x.EventId).ToArray();
            var existing = await db.SocEvents.AsNoTracking()
                .Where(x => x.EventId != null && ids.Contains(x.EventId.Value))
                .Select(x => x.EventId!.Value)
                .ToHashSetAsync(token);

            var now = DateTimeOffset.UtcNow;
            foreach (var item in batch)
            {
                if (!existing.Contains(item.EventId))
                    db.SocEvents.Add(ToEvent(item));

                item.ProcessedAtUtc = now;
                item.NextAttemptAtUtc = null;
                item.LastError = null;
            }

            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // One bad row used to fail (and eventually dead-letter) the whole batch of 100. Retry
            // the batch row by row so only the poison row is held back.
            if (batch.Length > 1)
            {
                logger.LogWarning(ex, "SOC outbox batch failed; retrying {Count} rows individually.", batch.Length);
                await DispatchIndividuallyAsync(batch.Select(x => x.Id).ToArray(), token);
                return batch.Length;
            }

            if (batch.Length > 0)
                await RecordFailureAsync(batch.Select(x => x.Id).ToArray(), ex, token);
            throw;
        }

        await BroadcastAsync(batch);
        return batch.Length;
    }

    private async Task DispatchIndividuallyAsync(long[] ids, CancellationToken token)
    {
        foreach (var id in ids)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await using var transaction = await db.Database.BeginTransactionAsync(token);

                // Not composed further (no LIMIT wrapper), so the row lock stays at the top level.
                var rows = await db.SocEventOutbox
                    .FromSqlInterpolated($"""
                        SELECT * FROM "SocEventOutbox"
                        WHERE "Id" = {id} AND "ProcessedAtUtc" IS NULL
                        FOR UPDATE SKIP LOCKED
                        """)
                    .AsTracking()
                    .ToArrayAsync(token);
                var item = rows.FirstOrDefault();

                if (item is null)
                {
                    await transaction.CommitAsync(token);
                    continue;
                }

                if (!await db.SocEvents.AsNoTracking().AnyAsync(x => x.EventId == item.EventId, token))
                    db.SocEvents.Add(ToEvent(item));

                item.ProcessedAtUtc = DateTimeOffset.UtcNow;
                item.NextAttemptAtUtc = null;
                item.LastError = null;
                await db.SaveChangesAsync(token);
                await transaction.CommitAsync(token);
                await BroadcastAsync([item]);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await RecordFailureAsync([id], ex, token);
            }
        }
    }

    private static SocEvent ToEvent(SocEventOutboxEntry item) => new()
    {
        EventId = item.EventId,
        TimestampUtc = item.OccurredAtUtc,
        Severity = item.Severity,
        Category = item.Category,
        EventType = item.EventType,
        Source = item.Source,
        Details = item.Details,
        CorrelationId = item.CorrelationId,
        PayloadJson = item.PayloadJson,
        TeamId = item.TeamId,
        RelatedTeamId = item.RelatedTeamId,
        ActorUserId = item.ActorUserId,
        UserId = item.UserId,
        GameId = item.GameId,
        ChallengeId = item.ChallengeId,
        InstanceId = item.InstanceId,
        SubmissionId = item.SubmissionId
    };

    // Live delivery is best-effort. A disconnected SignalR client must never make a committed
    // database event look unprocessed or poison a different outbox row.
    private async Task BroadcastAsync(IEnumerable<SocEventOutboxEntry> items)
    {
        foreach (var item in items)
            try
            {
                await hub.Clients.All.ReceivedSocUpdate(new(
                    "event",
                    item.OccurredAtUtc,
                    JsonSerializer.SerializeToElement(new SocUpdateDataModel(
                        item.EventId,
                        item.EventType,
                        item.Severity.ToString(),
                        item.Category.ToString(),
                        item.Source,
                        item.ActorUserId,
                        item.UserId,
                        item.TeamId,
                        item.GameId,
                        item.ChallengeId,
                        item.InstanceId), AppJsonSerializerContext.Default.SocUpdateDataModel)));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Unable to broadcast SOC event {EventId} to live administrators.", item.EventId);
            }
    }

    private async Task RecordFailureAsync(long[] ids, Exception exception, CancellationToken token)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var items = await db.SocEventOutbox
                .Where(x => ids.Contains(x.Id) && x.ProcessedAtUtc == null && x.AttemptCount < 10)
                .ToArrayAsync(token);

            foreach (var item in items)
            {
                item.AttemptCount++;
                item.LastError = Limit(exception.GetBaseException().Message, 2000);
                item.NextAttemptAtUtc = DateTimeOffset.UtcNow.AddSeconds(
                    Math.Min(300, Math.Pow(2, Math.Min(item.AttemptCount, 8))));
            }

            await db.SaveChangesAsync(token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Unable to record SOC outbox retry state.");
        }
    }

    private static string Limit(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
