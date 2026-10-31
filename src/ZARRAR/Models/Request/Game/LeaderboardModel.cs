using System.ComponentModel.DataAnnotations;

namespace ZARRAR.Models.Request.Game;

/// <summary>
/// A single row on the leaderboard.
/// </summary>
/// <remarks>
/// Deliberately a trimmed projection of <see cref="ScoreboardItem" />. The full item carries
/// SolvedChallenges — every solve, for every team — which the leaderboard never renders. On a
/// game with a few hundred teams that list dominates the response, so it is dropped here and
/// only the solve *count* is kept.
/// </remarks>
public class LeaderboardEntryModel
{
    [Required] public int Id { get; set; }

    [Required] public string Name { get; set; } = string.Empty;

    public string? Bio { get; set; }

    public string? Avatar { get; set; }

    public int? DivisionId { get; set; }

    [Required] public int Score { get; set; }

    [Required] public int Rank { get; set; }

    public int? DivisionRank { get; set; }

    [Required] public int SolvedCount { get; set; }

    [Required] public DateTimeOffset LastSubmissionTime { get; set; }

    public static LeaderboardEntryModel FromScoreboardItem(ScoreboardItem item) =>
        new()
        {
            Id = item.Id,
            Name = item.Name,
            Bio = item.Bio,
            Avatar = item.Avatar,
            DivisionId = item.DivisionId,
            Score = item.Score,
            Rank = item.Rank,
            DivisionRank = item.DivisionRank,
            SolvedCount = item.SolvedCount,
            LastSubmissionTime = item.LastSubmissionTime
        };
}

/// <summary>
/// The leaderboard's initial payload: the ranked page plus the caller's own team.
/// </summary>
public class LeaderboardModel
{
    /// <summary>
    /// Ranked entries, starting at rank 1. The client renders 1–3 as the podium and the
    /// remainder as the table; it needs all of them to switch between the two views without
    /// a second request.
    /// </summary>
    [Required]
    public IEnumerable<LeaderboardEntryModel> Entries { get; set; } = [];

    /// <summary>
    /// The caller's own team, present only when that team exists and falls outside
    /// <see cref="Entries" />. Lets the client pin a persistent "your team" row without
    /// downloading the entire ranking to find it.
    /// </summary>
    public LeaderboardEntryModel? CurrentTeam { get; set; }

    /// <summary>
    /// Total ranked teams in the game (or division), so the client can say "20 of 143".
    /// </summary>
    [Required]
    public int TotalCount { get; set; }

    [Required] public DateTimeOffset UpdateTimeUtc { get; set; }
}
