using System.ComponentModel.DataAnnotations;

namespace ZARRAR.Models.Request.Dashboard;

/// <summary>
/// Challenge and solve counts for one category of a game
/// </summary>
/// <remarks>
/// Derived from the cached scoreboard, which is the platform's source of truth for solve counts
/// and dynamic scores. Because the scoreboard is built from enabled challenges only, disabled
/// challenges can never appear here.
/// </remarks>
public class CategoryStatisticsModel
{
    /// <summary>
    /// The challenge category
    /// </summary>
    [Required]
    public ChallengeCategory Category { get; set; }

    /// <summary>
    /// Number of enabled challenges in this category
    /// </summary>
    [Required]
    public int ChallengeCount { get; set; }

    /// <summary>
    /// Number of teams that solved a challenge in this category, summed over its challenges
    /// </summary>
    [Required]
    public int SolvedCount { get; set; }

    /// <summary>
    /// Sum of the current dynamic score of every challenge in this category
    /// </summary>
    [Required]
    public int TotalScore { get; set; }
}
