using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ZARRAR.Models.Request.Edit;

namespace ZARRAR.Models.Data;

public class GameChallenge : Challenge
{
    /// <summary>
    /// Point deductions for hints, mapped to <see cref="Challenge.Hints"/> by index.
    /// A missing value is treated as zero so existing challenges keep their current behaviour.
    /// </summary>
    public List<int>? HintPenalties { get; set; }

    /// <summary>
    /// Whether to record traffic
    /// </summary>
    public bool EnableTrafficCapture { get; set; }

    /// <summary>
    /// Whether to disable blood bonus
    /// </summary>
    public bool DisableBloodBonus { get; set; }

    /// <summary>
    /// Initial score
    /// </summary>
    [Required]
    public int OriginalScore { get; set; } = 1000;

    /// <summary>
    /// Minimum score rate
    /// </summary>
    [Required]
    [Range(0, 1)]
    public double MinScoreRate { get; set; } = 0.25;

    /// <summary>
    /// Difficulty coefficient
    /// </summary>
    [Required]
    public double Difficulty { get; set; } = 5;

    /// <summary>
    /// Current score of the challenge
    /// </summary>
    [NotMapped]
    public int CurrentScore => CalculateChallengeScore(
        OriginalScore,
        MinScoreRate,
        Difficulty,
        FirstSolves?.Count ?? 0);


    internal static int CalculateChallengeScore(int originalScore, double minScoreRate, double difficulty,
        int acceptedCount)
    {
        if (acceptedCount <= 1)
            return originalScore;

        return (int)Math.Floor(
            originalScore *
            (minScoreRate + (1.0 - minScoreRate) * Math.Exp((1 - acceptedCount) / difficulty)));
    }

    internal void Update(ChallengeUpdateModel model)
    {
        Title = model.Title ?? Title;
        Content = model.Content ?? Content;
        Category = model.Category ?? Category;
        Hints = model.Hints ?? Hints;
        HintPenalties = model.HintPenalties ?? HintPenalties;
        CPUCount = model.CPUCount ?? CPUCount;
        MemoryLimit = model.MemoryLimit ?? MemoryLimit;
        StorageLimit = model.StorageLimit ?? StorageLimit;
        ContainerImage = model.ContainerImage?.Trim() ?? ContainerImage;
        ExposePort = model.ExposePort ?? ExposePort;
        NetworkMode = model.NetworkMode ?? NetworkMode;
        OriginalScore = model.OriginalScore ?? OriginalScore;
        MinScoreRate = model.MinScoreRate ?? MinScoreRate;
        Difficulty = model.Difficulty ?? Difficulty;
        FileName = model.FileName ?? FileName;
        DisableBloodBonus = model.DisableBloodBonus ?? DisableBloodBonus;
        SubmissionLimit = model.SubmissionLimit ?? SubmissionLimit;

        // isEnabled should be updated alone
        IsEnabled = model.IsEnabled ?? IsEnabled;

        // only set DeadlineUtc to null when pass DateTimeOffset.MinValue (but not null)
        if (model.DeadlineUtc is { } time)
            DeadlineUtc = time.ToUnixTimeSeconds() == 0 ? null : time;

        // only set FlagTemplate to null when pass an empty string (but not null)
        if (model.FlagTemplate is { } template)
            FlagTemplate = string.IsNullOrWhiteSpace(template) ? null : template;

        // Container only
        EnableTrafficCapture = Type.IsContainer() && (model.EnableTrafficCapture ?? EnableTrafficCapture);
    }

    internal int GetHintPenalty(int hintIndex) =>
        HintPenalties is { Count: > 0 } && hintIndex >= 0 && hintIndex < HintPenalties.Count
            ? Math.Max(0, HintPenalties[hintIndex])
            : 0;

    /// <summary>
    /// Determine whether an updated hint list retains the identity of every existing hint.
    /// Once a team has purchased a hint, its content and index must stay stable because the
    /// unlock record stores the index rather than a copy of the hint text.
    /// </summary>
    internal static bool KeepsPurchasedHintsStable(IReadOnlyList<string> existingHints,
        IReadOnlyList<string> updatedHints) =>
        updatedHints.Count >= existingHints.Count &&
        existingHints.SequenceEqual(updatedHints.Take(existingHints.Count));

    #region Db Relationship

    /// <summary>
    /// Submissions
    /// </summary>
    public List<Submission> Submissions { get; set; } = [];

    /// <summary>
    /// Challenge instances
    /// </summary>
    public List<GameInstance> Instances { get; set; } = [];

    /// <summary>
    /// Durable personalized-static assignment provenance for this challenge.
    /// </summary>
    public List<VariantAssignment> VariantAssignments { get; set; } = [];

    /// <summary>Optional shared Web/API runtime served through the authenticated gateway.</summary>
    public SharedRuntimeConfiguration? SharedRuntime { get; set; }

    public List<VariantTemplate> VariantTemplates { get; set; } = [];

    public List<VariantDatasetItem> VariantDatasetItems { get; set; } = [];

    /// <summary>
    /// Teams that activated the challenge
    /// </summary>
    public HashSet<Participation> Teams { get; set; } = [];

    /// <summary>
    /// Configurations for divisions
    /// </summary>
    public HashSet<DivisionChallengeConfig> DivisionConfigs { get; set; } = [];

    /// <summary>
    /// First solves recorded for this challenge.
    /// </summary>
    public List<FirstSolve>? FirstSolves { get; set; } = [];

    /// <summary>
    /// Team hint unlocks for this challenge.
    /// </summary>
    public List<HintUnlock> HintUnlocks { get; set; } = [];

    /// <summary>
    /// Game ID
    /// </summary>
    public int GameId { get; set; }

    /// <summary>
    /// Game object
    /// </summary>
    public Game Game { get; set; } = null!;

    #endregion Db Relationship
}
