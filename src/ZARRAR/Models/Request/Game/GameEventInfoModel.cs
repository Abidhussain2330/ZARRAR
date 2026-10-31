using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ZARRAR.Models.Request.Game;

/// <summary>
/// Redacted game event metadata for game monitors.
/// </summary>
public class GameEventInfoModel
{
    /// <summary>
    /// Event type
    /// </summary>
    [Required]
    public EventType Type { get; set; }

    /// <summary>
    /// Event values. Flag-submission answers are replaced by keyed fingerprints.
    /// </summary>
    [Required]
    public List<string> Values { get; set; } = [];

    /// <summary>
    /// Publish time
    /// </summary>
    [Required]
    [JsonPropertyName("time")]
    public DateTimeOffset PublishTimeUtc { get; set; }

    /// <summary>
    /// Related username
    /// </summary>
    [JsonPropertyName("user")]
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// Related team name
    /// </summary>
    [JsonPropertyName("team")]
    public string TeamName { get; set; } = string.Empty;

    internal static GameEventInfoModel FromGameEvent(GameEvent gameEvent, byte[]? fingerprintKey)
    {
        var values = gameEvent.Values?.ToList() ?? [];
        if (gameEvent.Type == EventType.FlagSubmit && values.Count > 1)
            values[1] = FlagFingerprint.Compute(values[1], fingerprintKey);

        return new()
        {
            Type = gameEvent.Type,
            Values = values,
            PublishTimeUtc = gameEvent.PublishTimeUtc,
            UserName = gameEvent.UserName,
            TeamName = gameEvent.TeamName
        };
    }
}
