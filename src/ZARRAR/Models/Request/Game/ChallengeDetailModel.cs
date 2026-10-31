using ZARRAR.Models.Request.Shared;

namespace ZARRAR.Models.Request.Game;

/// <summary>
/// Challenge detailed information
/// </summary>
public class ChallengeDetailModel
{
    /// <summary>
    /// Challenge ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Challenge title
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Challenge content
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Challenge category
    /// </summary>
    public ChallengeCategory Category { get; set; } = ChallengeCategory.Misc;

    /// <summary>
    /// Challenge hints. Paid hints remain hidden until the current team unlocks them.
    /// </summary>
    public List<ChallengeHintDetailModel> Hints { get; set; } = [];

    /// <summary>
    /// Current score of the challenge
    /// </summary>
    public int Score { get; set; }

    /// <summary>
    /// Challenge type
    /// </summary>
    public ChallengeType Type { get; set; } = ChallengeType.StaticAttachment;

    /// <summary>
    /// Flag context
    /// </summary>
    public ClientFlagContext Context { get; set; } = null!;

    /// <summary>
    /// Maximum number of attempts allowed (0 = no limit)
    /// </summary>
    public int Limit { get; set; }

    /// <summary>
    /// Current attempt count
    /// </summary>
    public int Attempts { get; set; }

    /// <summary>
    /// Deadline of the challenge, null means no deadline
    /// </summary>
    public DateTimeOffset? Deadline { get; set; }

    internal static ChallengeDetailModel FromInstance(GameInstance gameInstance, int attemptCount,
        ChallengeInfo? scoreboardChallenge = null, IReadOnlyDictionary<int, int>? unlockedHintPenalties = null)
    {
        var challenge = gameInstance.Challenge;
        var hints = challenge.Hints ?? [];
        var score = scoreboardChallenge?.Score ?? challenge.CurrentScore;
        var deducted = unlockedHintPenalties?.Values.Sum(value => Math.Max(0, value)) ?? 0;

        return new()
        {
            Id = challenge.Id,
            Content = challenge.Content,
            Hints = hints.Select((content, index) =>
            {
                var penalty = challenge.GetHintPenalty(index);
                var unlocked = penalty == 0 || unlockedHintPenalties?.ContainsKey(index) == true;
                return new ChallengeHintDetailModel
                {
                    Index = index,
                    Content = unlocked ? content : null,
                    Penalty = penalty,
                    IsUnlocked = unlocked
                };
            }).ToList(),
            Score = Math.Max(0, score - deducted),
            Category = challenge.Category,
            Title = challenge.Title,
            Type = challenge.Type,
            Limit = challenge.SubmissionLimit,
            Deadline = challenge.DeadlineUtc,
            Attempts = attemptCount,
            Context = new()
            {
                InstanceEntry = gameInstance.Container?.Entry,
                CloseTime = gameInstance.Container?.ExpectStopAt,
                Url = gameInstance.AttachmentUrl,
                FileSize = gameInstance.Attachment?.FileSize
            }
        };
    }
}

/// <summary>
/// Player-safe representation of a challenge hint.
/// </summary>
public class ChallengeHintDetailModel
{
    /// <summary>Zero-based index of the hint within the challenge.</summary>
    public int Index { get; set; }

    /// <summary>The hint text, withheld until a paid hint has been unlocked.</summary>
    public string? Content { get; set; }

    /// <summary>Points deducted from the team's award for this challenge.</summary>
    public int Penalty { get; set; }

    /// <summary>Whether the current team can see the hint text.</summary>
    public bool IsUnlocked { get; set; }
}
