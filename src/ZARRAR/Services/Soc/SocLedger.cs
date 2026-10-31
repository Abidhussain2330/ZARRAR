using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace ZARRAR.Services.Soc;

/// <summary>
/// S3 — tamper-evident SOC timeline, without a schema change.
/// </summary>
/// <remarks>
/// <para>
/// Every few minutes the newly committed <c>SocEvents</c> rows are folded, in Id order, into a
/// keyed hash chain (HMAC-SHA256), and a <c>SocLedgerSeal</c> event is written that records the
/// range, the count and the chain head. Each seal starts from the previous seal's head, so the
/// seals form one chain across the whole timeline.
/// </para>
/// <para>
/// Editing, deleting or back-dating any sealed row breaks the chain at that seal; the verify
/// endpoint (<c>GET /api/soc/ledger</c>) reports where. The HMAC key is derived from the platform
/// secret (or <c>Soc:LedgerKey</c>), so a database-only attacker cannot recompute valid seals.
/// </para>
/// <para>
/// Rows are sealed one cycle late: only Ids already seen in the previous cycle, so a transaction
/// that reserved an Id but committed a moment later never lands inside a closed range.
/// </para>
/// </remarks>
public static class SocLedger
{
    public const string SealEventType = "SocLedgerSeal";
    public const int MaxEventsPerSeal = 5000;

    public static byte[] DeriveKey(IConfiguration configuration)
    {
        var secret = configuration["Soc:LedgerKey"] is { Length: > 0 } ledgerKey
            ? ledgerKey
            : configuration["XorKey"] ?? string.Empty;
        return HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), "zarrar:soc-ledger:v1"u8);
    }

    /// <summary>Folds one event into the chain.</summary>
    public static string Next(byte[] key, string previousHead, SocEvent e)
    {
        var canonical = string.Join('\u001f',
            previousHead,
            e.Id.ToString(CultureInfo.InvariantCulture),
            e.EventId?.ToString("N") ?? "",
            e.TimestampUtc.UtcTicks.ToString(CultureInfo.InvariantCulture),
            ((int)e.Severity).ToString(CultureInfo.InvariantCulture),
            ((int)e.Category).ToString(CultureInfo.InvariantCulture),
            e.EventType,
            e.Source ?? "",
            e.Details,
            e.CorrelationId ?? "",
            e.PayloadJson ?? "",
            e.TeamId?.ToString(CultureInfo.InvariantCulture) ?? "",
            e.RelatedTeamId?.ToString(CultureInfo.InvariantCulture) ?? "",
            e.ActorUserId?.ToString("N") ?? "",
            e.UserId?.ToString("N") ?? "",
            e.GameId?.ToString(CultureInfo.InvariantCulture) ?? "",
            e.ChallengeId?.ToString(CultureInfo.InvariantCulture) ?? "",
            e.InstanceId?.ToString("N") ?? "",
            e.SubmissionId?.ToString(CultureInfo.InvariantCulture) ?? "");

        return Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(canonical)));
    }

    public sealed record SealPayload(long FromId, long ToId, int Count, string PreviousHead, string Head);

    public static SealPayload? ReadSeal(SocEvent seal)
    {
        if (seal.EventType != SealEventType || string.IsNullOrEmpty(seal.PayloadJson))
            return null;

        try
        {
            return JsonSerializer.Deserialize<SealPayload>(seal.PayloadJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public const string GenesisHead = "genesis";
}

/// <summary>Writes the seals (see <see cref="SocLedger" />).</summary>
public sealed class SocLedgerService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<SocLedgerService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);
    private long _seenUpTo = -1;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var key = SocLedger.DeriveKey(configuration);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SealAsync(key, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "SOC ledger sealing failed.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task SealAsync(byte[] key, CancellationToken token)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var maxId = await db.SocEvents.AsNoTracking().MaxAsync(e => (long?)e.Id, token) ?? 0;
        var cutoff = _seenUpTo;
        _seenUpTo = maxId;
        if (cutoff < 0)
            return; // first cycle: just remember where we are

        var lastSeal = await db.SocEvents.AsNoTracking()
            .Where(e => e.EventType == SocLedger.SealEventType)
            .OrderByDescending(e => e.Id)
            .FirstOrDefaultAsync(token);
        var previous = lastSeal is null ? null : SocLedger.ReadSeal(lastSeal);
        var fromExclusive = previous?.ToId ?? 0;
        var head = previous?.Head ?? SocLedger.GenesisHead;

        var events = await db.SocEvents.AsNoTracking()
            .Where(e => e.Id > fromExclusive && e.Id <= cutoff && e.EventType != SocLedger.SealEventType)
            .OrderBy(e => e.Id)
            .Take(SocLedger.MaxEventsPerSeal)
            .ToArrayAsync(token);
        if (events.Length == 0)
            return;

        var startHead = head;
        foreach (var e in events)
            head = SocLedger.Next(key, head, e);

        var payload = new SocLedger.SealPayload(events[0].Id, events[^1].Id, events.Length, startHead, head);
        db.SocEvents.Add(new SocEvent
        {
            EventId = Guid.NewGuid(),
            TimestampUtc = DateTimeOffset.UtcNow,
            Severity = SocSeverity.Info,
            Category = SocCategory.PlatformSecurity,
            EventType = SocLedger.SealEventType,
            Source = nameof(SocLedgerService),
            Details = $"Sealed SOC events {payload.FromId}–{payload.ToId} ({payload.Count}) into the ledger.",
            PayloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        });
        await db.SaveChangesAsync(token);
    }
}
