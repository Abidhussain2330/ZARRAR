using System.ComponentModel.DataAnnotations;

namespace BURAQ.Models.Request.Game;

/// <summary>
/// A container the caller's team currently has running.
/// </summary>
/// <remarks>
/// Instances are keyed by (ParticipationId, ChallengeId) — one per team per challenge, not one
/// per player. A container started by any team member is the same container everyone else
/// connects to, and <c>Game.ContainerCountLimit</c> caps how many the team may run at once.
/// That sharing is precisely why this endpoint exists: without it a player has no way to see
/// what teammates have already spun up, or how close the team is to its limit.
/// </remarks>
public class TeamInstanceModel
{
    [Required] public int ChallengeId { get; set; }

    [Required] public string ChallengeTitle { get; set; } = string.Empty;

    public string? Category { get; set; }

    /// <summary>
    /// Connection entry — host:port, or the proxy identifier when traffic proxying is enabled.
    /// </summary>
    [Required]
    public string Entry { get; set; } = string.Empty;

    [Required] public DateTimeOffset StartedAt { get; set; }

    /// <summary>
    /// When the container is scheduled to stop. Drives the countdown and the extend prompt.
    /// </summary>
    [Required]
    public DateTimeOffset ExpectStopAt { get; set; }
}

/// <summary>
/// The caller team's running containers, with the limit they count against.
/// </summary>
public class TeamInstancesModel
{
    [Required] public IEnumerable<TeamInstanceModel> Instances { get; set; } = [];

    /// <summary>
    /// Effective maximum concurrent containers for the team. A participation-level SOC override
    /// takes precedence over <c>Game.ContainerCountLimit</c>. Zero means unlimited.
    /// </summary>
    [Required]
    public int Limit { get; set; }
}
