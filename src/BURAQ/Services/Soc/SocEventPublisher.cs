using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace BURAQ.Services.Soc;

public sealed class SocEventPublisher(AppDbContext db, IHttpContextAccessor httpContextAccessor)
    : ISocEventPublisher
{
    private const int MaximumPayloadBytes = 8000;

    public Guid Enqueue(SocEventMessage message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message.EventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(message.Details);

        var eventId = message.EventId ?? Guid.NewGuid();
        var http = httpContextAccessor.HttpContext;
        var actorUserId = message.ActorUserId ?? TryGetCurrentUserId(http);
        var correlationId = message.CorrelationId
                            ?? Activity.Current?.TraceId.ToString()
                            ?? http?.TraceIdentifier;
        var payloadJson = ValidatePayload(message.PayloadJson);

        db.SocEventOutbox.Add(new SocEventOutboxEntry
        {
            EventId = eventId,
            OccurredAtUtc = message.OccurredAtUtc ?? DateTimeOffset.UtcNow,
            Severity = message.Severity,
            Category = message.Category,
            EventType = Limit(message.EventType, 80),
            Source = Limit(string.IsNullOrWhiteSpace(message.Source) ? "BURAQ" : message.Source, 80),
            Details = Limit(message.Details, 1000),
            CorrelationId = LimitNullable(correlationId, 128),
            PayloadJson = payloadJson,
            TeamId = message.TeamId,
            RelatedTeamId = message.RelatedTeamId,
            ActorUserId = actorUserId,
            UserId = message.UserId,
            GameId = message.GameId,
            ChallengeId = message.ChallengeId,
            InstanceId = message.InstanceId,
            SubmissionId = message.SubmissionId
        });

        return eventId;
    }

    public async Task<Guid> PublishAsync(SocEventMessage message, CancellationToken token = default)
    {
        var eventId = Enqueue(message);
        await db.SaveChangesAsync(token);
        return eventId;
    }

    private static Guid? TryGetCurrentUserId(HttpContext? context)
        => Guid.TryParse(context?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private static string? ValidatePayload(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return null;

        try
        {
            using var _ = JsonDocument.Parse(payloadJson);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("SOC event PayloadJson must contain valid JSON.", nameof(payloadJson), ex);
        }

        if (Encoding.UTF8.GetByteCount(payloadJson) > MaximumPayloadBytes)
            throw new ArgumentException(
                $"SOC event PayloadJson must not exceed {MaximumPayloadBytes} UTF-8 bytes.",
                nameof(payloadJson));

        return payloadJson;
    }

    private static string Limit(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private static string? LimitNullable(string? value, int maxLength)
        => string.IsNullOrEmpty(value) ? value : Limit(value, maxLength);
}
