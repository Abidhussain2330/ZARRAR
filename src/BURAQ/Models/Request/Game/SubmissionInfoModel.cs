using System.Text.Json.Serialization;

namespace BURAQ.Models.Request.Game;

/// <summary>
/// Redacted submission metadata for game monitors.
/// </summary>
public class SubmissionInfoModel
{
    /// <summary>
    /// Non-reversible, keyed fingerprint of the submitted answer
    /// </summary>
    public string Fingerprint { get; set; } = FlagFingerprint.Empty;

    /// <summary>
    /// Status of the submitted answer
    /// </summary>
    public AnswerResult Status { get; set; }

    /// <summary>
    /// Time the answer was submitted
    /// </summary>
    [JsonPropertyName("time")]
    public DateTimeOffset SubmitTimeUtc { get; set; }

    /// <summary>
    /// User who submitted
    /// </summary>
    [JsonPropertyName("user")]
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// Team that submitted
    /// </summary>
    [JsonPropertyName("team")]
    public string TeamName { get; set; } = string.Empty;

    /// <summary>
    /// Challenge that was submitted
    /// </summary>
    [JsonPropertyName("challenge")]
    public string ChallengeName { get; set; } = string.Empty;

    internal static SubmissionInfoModel FromSubmission(Submission submission, byte[]? fingerprintKey) =>
        new()
        {
            Fingerprint = FlagFingerprint.Compute(submission.Answer, fingerprintKey),
            Status = submission.Status,
            SubmitTimeUtc = submission.SubmitTimeUtc,
            UserName = submission.UserName,
            TeamName = submission.TeamName,
            ChallengeName = submission.ChallengeName
        };
}
