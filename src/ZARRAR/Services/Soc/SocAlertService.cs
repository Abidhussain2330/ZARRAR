using System.Text.Json;
using ZARRAR.Hubs;
using ZARRAR.Hubs.Clients;
using ZARRAR.Models.Request.Admin;
using ZARRAR.Utils;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ZARRAR.Services.Soc;

public sealed class SocAlertService(
    AppDbContext db,
    ISocEventPublisher eventPublisher,
    IHubContext<AdminHub, IAdminClient> hub) : ISocAlertService
{
    public async Task<SocAlert?> CreateAsync(SocAlert alert, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alert.SourceKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(alert.Event);

        if (await db.SocAlerts.AsNoTracking().AnyAsync(x => x.SourceKey == alert.SourceKey, token))
            return null;

        alert.CreatedAtUtc = alert.CreatedAtUtc == default ? DateTimeOffset.UtcNow : alert.CreatedAtUtc;
        alert.UpdatedAtUtc = DateTimeOffset.UtcNow;
        db.SocAlerts.Add(alert);

        var eventId = eventPublisher.Enqueue(new SocEventMessage(
            EventType: "SocAlertCreated",
            Category: alert.Category,
            Severity: alert.Severity,
            Details: $"SOC alert '{alert.Event}' was created.",
            Source: nameof(SocAlertService),
            TeamId: alert.TeamId,
            RelatedTeamId: alert.RelatedTeamId,
            GameId: alert.GameId,
            ChallengeId: alert.ChallengeId,
            InstanceId: alert.InstanceId,
            SubmissionId: alert.SubmissionId,
            PayloadJson: JsonSerializer.Serialize(new
            {
                alert.SourceKey,
                alert.RiskImpact,
                alert.EvidenceFingerprint
            })));

        try
        {
            // The alert and its audit event are persisted in one database transaction by SaveChanges.
            await db.SaveChangesAsync(token);
        }
        catch (DbUpdateException)
        {
            db.Entry(alert).State = EntityState.Detached;
            var outboxEntry = db.ChangeTracker.Entries<SocEventOutboxEntry>()
                .FirstOrDefault(x => x.Entity.EventId == eventId);
            if (outboxEntry is not null)
                outboxEntry.State = EntityState.Detached;

            if (await db.SocAlerts.AsNoTracking().AnyAsync(x => x.SourceKey == alert.SourceKey, token))
                return null;
            throw;
        }

        await hub.Clients.All.ReceivedSocUpdate(new(
            "alert",
            DateTimeOffset.UtcNow,
            JsonSerializer.SerializeToElement(new SocAlertUpdateDataModel(
                alert.Id,
                alert.Event,
                alert.Severity.ToString(),
                alert.Category.ToString(),
                alert.TeamId,
                alert.GameId,
                alert.ChallengeId,
                alert.RiskImpact), AppJsonSerializerContext.Default.SocAlertUpdateDataModel)));

        return alert;
    }
}
