using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using BURAQ.Models.Request.Game;

namespace BURAQ.Models.Request.Dashboard;

/// <summary>
/// The calling user's own standing in the relevant game
/// </summary>
/// <remarks>
/// Every field is about the caller and the caller's own team. Nothing about any other participant
/// is carried here. When the caller has not joined a game — or there is no relevant game at all —
/// the model is returned with <see cref="Registered" /> false and the scoring fields at zero,
/// rather than as an error.
/// </remarks>
public class DashboardUserStateModel
{
    /// <summary>
    /// The game this standing refers to, null when there is no relevant game
    /// </summary>
    public int? GameId { get; set; }

    /// <summary>
    /// Title of the game this standing refers to, null when there is no relevant game
    /// </summary>
    public string? GameTitle { get; set; }

    /// <summary>
    /// Whether the caller has joined the game with a team
    /// </summary>
    [Required]
    public bool Registered { get; set; }

    /// <summary>
    /// Whether the scoring fields below are populated, i.e. the caller's registration was accepted
    /// and the scoreboard has been built
    /// </summary>
    [Required]
    public bool Ranked { get; set; }

    /// <summary>
    /// The caller's registration status
    /// </summary>
    [Required]
    [JsonPropertyName("status")]
    public ParticipationStatus Status { get; set; } = ParticipationStatus.Unsubmitted;

    /// <summary>
    /// The caller's team ID, null when not registered
    /// </summary>
    public int? TeamId { get; set; }

    /// <summary>
    /// The caller's team name, null when not registered
    /// </summary>
    public string? TeamName { get; set; }

    /// <summary>
    /// The caller's team avatar URL
    /// </summary>
    public string? TeamAvatar { get; set; }

    /// <summary>
    /// The division the caller's team competes in, null when the game has no divisions
    /// </summary>
    public int? DivisionId { get; set; }

    /// <summary>
    /// Overall rank of the caller's team; 0 when the team is not ranked overall
    /// </summary>
    [Required]
    public int Rank { get; set; }

    /// <summary>
    /// Rank of the caller's team within its division, null when the game has no divisions
    /// </summary>
    public int? DivisionRank { get; set; }

    /// <summary>
    /// Total score of the caller's team
    /// </summary>
    [Required]
    public int Score { get; set; }

    /// <summary>
    /// Number of challenges the caller's team has solved
    /// </summary>
    [Required]
    public int SolvedCount { get; set; }

    /// <summary>
    /// Time of the team's last scoring submission, null when the team has not scored
    /// </summary>
    [JsonPropertyName("lastSubmission")]
    public DateTimeOffset? LastSubmissionTimeUtc { get; set; }

    /// <summary>
    /// Fill in the scoring fields from the team's scoreboard row.
    /// </summary>
    internal DashboardUserStateModel WithScoreboardItem(ScoreboardItem item)
    {
        Ranked = true;
        Rank = item.Rank;
        DivisionRank = item.DivisionRank;
        DivisionId = item.DivisionId;
        Score = item.Score;
        SolvedCount = item.SolvedCount;
        TeamAvatar = item.Avatar;
        // The scoreboard uses MinValue as "has not scored yet"; the dashboard reports that as null
        // rather than as a date in the year 1.
        LastSubmissionTimeUtc = item.LastSubmissionTime == DateTimeOffset.MinValue
            ? null
            : (DateTimeOffset?)item.LastSubmissionTime;
        return this;
    }
}
