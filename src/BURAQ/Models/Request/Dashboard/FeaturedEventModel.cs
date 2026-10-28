using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace BURAQ.Models.Request.Dashboard;

/// <summary>
/// The single most relevant game for the calling user
/// </summary>
/// <remarks>
/// Only the fields the game listing already publishes are carried here. The signing key pair, the
/// invite code and the private notes on <see cref="Data.Game" /> are never projected into this
/// model.
/// </remarks>
public class FeaturedEventModel
{
    /// <summary>
    /// Game ID
    /// </summary>
    [Required]
    public int Id { get; set; }

    /// <summary>
    /// Game title
    /// </summary>
    [Required]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Game summary
    /// </summary>
    [Required]
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Poster image URL
    /// </summary>
    [JsonPropertyName("poster")]
    public string? PosterUrl { get; set; }

    /// <summary>
    /// Start time
    /// </summary>
    [Required]
    [JsonPropertyName("start")]
    public DateTimeOffset StartTimeUtc { get; set; }

    /// <summary>
    /// End time
    /// </summary>
    [Required]
    [JsonPropertyName("end")]
    public DateTimeOffset EndTimeUtc { get; set; }

    /// <summary>
    /// Whether the game has started and not yet ended
    /// </summary>
    [Required]
    public bool IsActive { get; set; }

    /// <summary>
    /// Whether the game can still be played after the end time
    /// </summary>
    [Required]
    public bool PracticeMode { get; set; }

    /// <summary>
    /// Team member count limit, 0 means no limit
    /// </summary>
    [Required]
    [JsonPropertyName("limit")]
    public int TeamMemberCountLimit { get; set; }

    /// <summary>
    /// Number of teams registered for the game
    /// </summary>
    [Required]
    public int TeamCount { get; set; }

    /// <summary>
    /// The calling user's registration status for this game
    /// </summary>
    [Required]
    [JsonPropertyName("status")]
    public ParticipationStatus Status { get; set; } = ParticipationStatus.Unsubmitted;

    /// <summary>
    /// Whether the calling user has joined the game with a team
    /// </summary>
    [Required]
    public bool Registered { get; set; }

    /// <summary>
    /// Name of the team the calling user joined with, null when not registered
    /// </summary>
    public string? TeamName { get; set; }

    internal static FeaturedEventModel FromGame(Data.Game game, int teamCount, Participation? part) =>
        new()
        {
            Id = game.Id,
            Title = game.Title,
            Summary = game.Summary,
            PosterUrl = game.PosterUrl,
            StartTimeUtc = game.StartTimeUtc,
            EndTimeUtc = game.EndTimeUtc,
            IsActive = game.IsActive,
            PracticeMode = game.PracticeMode,
            TeamMemberCountLimit = game.TeamMemberCountLimit,
            TeamCount = teamCount,
            Registered = part is not null,
            Status = part?.Status ?? ParticipationStatus.Unsubmitted,
            TeamName = part?.Team.Name
        };
}
