using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using MemoryPack;

namespace ZARRAR.Models.Request.Dashboard;

/// <summary>
/// A single accepted submission in the dashboard live solve feed
/// </summary>
/// <remarks>
/// The feed deliberately carries no submission identifier and no answer text. It exposes exactly
/// the fields the public scoreboard already exposes — solver name, team, challenge, category,
/// score and blood status — and nothing else.
/// </remarks>
[MemoryPackable]
public partial class LiveSolveModel
{
    /// <summary>
    /// Time the flag was submitted
    /// </summary>
    [Required]
    [JsonPropertyName("time")]
    public DateTimeOffset SubmitTimeUtc { get; set; }

    /// <summary>
    /// Display name of the solver, null when the account has since been removed
    /// </summary>
    [JsonPropertyName("user")]
    public string? UserName { get; set; }

    /// <summary>
    /// Name of the team the solve counted for
    /// </summary>
    [Required]
    [JsonPropertyName("team")]
    public string TeamName { get; set; } = string.Empty;

    /// <summary>
    /// ID of the game the solve belongs to
    /// </summary>
    [Required]
    public int GameId { get; set; }

    /// <summary>
    /// Title of the game the solve belongs to
    /// </summary>
    [Required]
    public string GameTitle { get; set; } = string.Empty;

    /// <summary>
    /// ID of the solved challenge
    /// </summary>
    [Required]
    public int ChallengeId { get; set; }

    /// <summary>
    /// Title of the solved challenge
    /// </summary>
    [Required]
    [JsonPropertyName("challenge")]
    public string ChallengeTitle { get; set; } = string.Empty;

    /// <summary>
    /// Category of the solved challenge
    /// </summary>
    [Required]
    public ChallengeCategory Category { get; set; }

    /// <summary>
    /// Current dynamic score of the solved challenge
    /// </summary>
    [Required]
    public int Score { get; set; }

    /// <summary>
    /// Whether this submission was the first solve of the challenge by any team
    /// </summary>
    [Required]
    public bool IsFirstBlood { get; set; }
}
