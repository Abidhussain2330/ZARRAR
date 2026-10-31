using System.Collections.Concurrent;
using System.Text.Json;

namespace ZARRAR.Services.Soc;

/// <summary>
/// Feeds security signals that happen outside a request's normal flow (rate-limit rejections,
/// cross-origin rejections, showdown anti-cheat events) into the SOC timeline.
/// </summary>
/// <remarks>
/// Every signal carries a throttle key. The same key is published at most once per
/// <see cref="Window" />, with a count of the repeats that were folded into it, so an attacker
/// hammering an endpoint produces one timeline row per window instead of flooding the database
/// (which would itself be a denial of service). Publishing is fire-and-forget on its own scope and
/// never throws into the caller.
/// </remarks>
public sealed class SecuritySignalSink(IServiceScopeFactory scopeFactory, ILogger<SecuritySignalSink> logger)
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
    private const int MaxTrackedKeys = 20_000;

    private readonly ConcurrentDictionary<string, Slot> _slots = new();

    /// <summary>Publishes the signal unless the same key was published within the window.</summary>
    public void Signal(string throttleKey, SocEventMessage message, Func<int, SocAlert?>? escalate = null)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var slot = _slots.GetOrAdd(throttleKey, _ => new Slot());
            int folded;

            lock (slot)
            {
                slot.Total++;
                if (now - slot.LastPublishedUtc < Window)
                {
                    slot.Folded++;
                    return;
                }

                folded = slot.Folded;
                slot.Folded = 0;
                slot.LastPublishedUtc = now;
            }

            if (_slots.Count > MaxTrackedKeys)
                Prune(now);

            var total = slot.Total;
            var payload = Merge(message.PayloadJson, folded, total);
            var alert = escalate?.Invoke(total);

            _ = Task.Run(async () =>
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var events = scope.ServiceProvider.GetRequiredService<ISocEventPublisher>();
                    await events.PublishAsync(message with { PayloadJson = payload });

                    if (alert is not null)
                        await scope.ServiceProvider.GetRequiredService<ISocAlertService>().CreateAsync(alert);
                }
                catch (Exception e)
                {
                    logger.LogWarning(e, "Could not record security signal {EventType}.", message.EventType);
                }
            });
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Security signal sink failed.");
        }
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var (key, slot) in _slots)
            if (now - slot.LastPublishedUtc > Window * 3)
                _slots.TryRemove(key, out _);
    }

    private static string Merge(string? payloadJson, int folded, int total)
    {
        var extra = new Dictionary<string, object?> { ["repeatsFolded"] = folded, ["totalSeen"] = total };

        if (!string.IsNullOrWhiteSpace(payloadJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    foreach (var prop in doc.RootElement.EnumerateObject())
                        extra.TryAdd(prop.Name, prop.Value.Clone());
            }
            catch (JsonException)
            {
                // the original payload is dropped rather than failing the signal
            }
        }

        var json = JsonSerializer.Serialize(extra);
        return json.Length <= 7000 ? json : JsonSerializer.Serialize(new { repeatsFolded = folded, totalSeen = total });
    }

    private sealed class Slot
    {
        public DateTimeOffset LastPublishedUtc = DateTimeOffset.MinValue;
        public int Folded;
        public int Total;
    }
}
