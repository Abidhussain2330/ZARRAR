using System.ComponentModel.DataAnnotations;
using MemoryPack;

namespace ZARRAR.Models.Request.Dashboard;

/// <summary>
/// Platform-wide counters for the command centre dashboard
/// </summary>
/// <remarks>
/// Every counter is scoped to what the caller is allowed to see: for anyone below
/// <see cref="Role.Admin" /> hidden games and disabled challenges are excluded from all of them.
/// The aggregate is cached, so the counters are accurate as of <see cref="UpdateTimeUtc" /> rather
/// than as of the request.
/// </remarks>
[MemoryPackable]
public partial class DashboardOverviewModel
{
    /// <summary>
    /// Number of games that have started and not yet ended
    /// </summary>
    [Required]
    public int ActiveGameCount { get; set; }

    /// <summary>
    /// Number of games whose start time is still in the future
    /// </summary>
    [Required]
    public int UpcomingGameCount { get; set; }

    /// <summary>
    /// Number of enabled challenges across all visible games
    /// </summary>
    [Required]
    public int ChallengeCount { get; set; }

    /// <summary>
    /// Number of distinct users registered for at least one visible game
    /// </summary>
    [Required]
    public int ParticipantCount { get; set; }

    /// <summary>
    /// Number of team registrations across all visible games
    /// </summary>
    [Required]
    public int TeamCount { get; set; }

    /// <summary>
    /// Number of accepted submissions in the last 24 hours across all visible games
    /// </summary>
    [Required]
    public int RecentSolveCount { get; set; }

    /// <summary>
    /// The time the counters were computed
    /// </summary>
    [Required]
    public DateTimeOffset UpdateTimeUtc { get; set; } = DateTimeOffset.UtcNow;
}
