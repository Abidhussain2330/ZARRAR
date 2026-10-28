using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using BURAQ.Models.Request.Game;

namespace BURAQ.Models.Request.Dashboard;

/// <summary>
/// One entry at the head of a game's leaderboard
/// </summary>
/// <remarks>
/// Projected from <see cref="ScoreboardItem" /> so the dashboard shows exactly the ranking the
/// scoreboard shows; no score is recomputed here. Teams the scoreboard left unranked — because
/// their division does not carry <see cref="GamePermission.RankOverall" /> — are not included.
/// </remarks>
public class TopParticipantModel
{
    /// <summary>
    /// Overall rank in the game
    /// </summary>
    [Required]
    public int Rank { get; set; }

    /// <summary>
    /// Team ID
    /// </summary>
    [Required]
    public int TeamId { get; set; }

    /// <summary>
    /// Team name
    /// </summary>
    [Required]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Team avatar URL
    /// </summary>
    public string? Avatar { get; set; }

    /// <summary>
    /// Total score
    /// </summary>
    [Required]
    public int Score { get; set; }

    /// <summary>
    /// Number of solved challenges
    /// </summary>
    [Required]
    public int SolvedCount { get; set; }

    /// <summary>
    /// Division the team competes in, null when the game has no divisions
    /// </summary>
    public int? DivisionId { get; set; }

    /// <summary>
    /// Rank within the division, null when the game has no divisions
    /// </summary>
    public int? DivisionRank { get; set; }

    /// <summary>
    /// Time of the team's last scoring submission
    /// </summary>
    [Required]
    [JsonPropertyName("lastSubmission")]
    public DateTimeOffset LastSubmissionTimeUtc { get; set; }

    internal static TopParticipantModel FromScoreboardItem(ScoreboardItem item) =>
        new()
        {
            Rank = item.Rank,
            TeamId = item.Id,
            Name = item.Name,
            Avatar = item.Avatar,
            Score = item.Score,
            SolvedCount = item.SolvedCount,
            DivisionId = item.DivisionId,
            DivisionRank = item.DivisionRank,
            LastSubmissionTimeUtc = item.LastSubmissionTime
        };
}
