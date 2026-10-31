using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace ZARRAR.Models.Data;

/// <summary>
/// Immutable record of a team's purchase of a paid challenge hint.
/// </summary>
[Index(nameof(ChallengeId))]
[PrimaryKey(nameof(ParticipationId), nameof(ChallengeId), nameof(HintIndex))]
public class HintUnlock
{
    /// <summary>Team participation that unlocked the hint.</summary>
    [Required]
    public int ParticipationId { get; set; }

    /// <summary>Challenge that owns the hint.</summary>
    [Required]
    public int ChallengeId { get; set; }

    /// <summary>Zero-based position of the hint within the challenge.</summary>
    [Required]
    public int HintIndex { get; set; }

    /// <summary>Point deduction captured when the hint was unlocked.</summary>
    [Range(0, int.MaxValue)]
    public int Penalty { get; set; }

    /// <summary>When the hint was unlocked.</summary>
    [Required]
    public DateTimeOffset UnlockedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>User who initiated the unlock, retained for auditing when available.</summary>
    public Guid? UserId { get; set; }

    public Participation Participation { get; set; } = null!;

    public GameChallenge Challenge { get; set; } = null!;

    public UserInfo? User { get; set; }
}
