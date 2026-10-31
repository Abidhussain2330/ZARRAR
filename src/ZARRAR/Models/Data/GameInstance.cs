using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace ZARRAR.Models.Data;

[Index(nameof(FlagId), IsUnique = true)]
[PrimaryKey(nameof(ParticipationId), nameof(ChallengeId))]
public class GameInstance : Instance
{
    /// <summary>
    /// Get instance attachment
    /// </summary>
    internal Attachment? Attachment
    {
        get => Challenge.Type == ChallengeType.DynamicAttachment
            ? FlagContext?.Attachment
            : Challenge.Attachment;
    }

    /// <summary>
    /// Get instance attachment URL
    /// </summary>
    internal string? AttachmentUrl
    {
        get => Challenge.Type == ChallengeType.DynamicAttachment
            ? PersonalizedArtifactRoutes.ForGame(Challenge.GameId, ChallengeId)
            : Challenge.Attachment is { Type: not FileType.None }
                ? ChallengeAttachmentRoutes.ForGame(Challenge.GameId, ChallengeId)
                : null;
    }

    #region Db Relationship

    [Required]
    public int ChallengeId { get; set; }

    /// <summary>
    /// Challenge object
    /// </summary>
    public GameChallenge Challenge { get; set; } = null!;

    [Required]
    public int ParticipationId { get; set; }

    /// <summary>
    /// Participation team object
    /// </summary>
    public Participation Participation { get; set; } = null!;

    #endregion Db Relationship
}
