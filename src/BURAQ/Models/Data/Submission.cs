using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace BURAQ.Models.Data;

[Index(nameof(UserId))]
[Index(nameof(TeamId), nameof(ChallengeId), nameof(GameId))]
// Supports the two time-ordered reads over accepted submissions that the dashboard performs: the
// live solve feed, which is "the newest N accepted submissions", and the count of solves in the
// last 24 hours. Neither has an index to work with otherwise — the existing indexes all lead with
// an entity id — so both degrade into a full scan of what is the largest table on the platform,
// and the feed additionally sorts it. Leading with Status keeps the scan inside the accepted rows;
// SubmitTimeUtc then supplies both the ordering and the range bound.
[Index(nameof(Status), nameof(SubmitTimeUtc))]
public class Submission
{
    [Key]
    [JsonIgnore]
    public int Id { get; set; }

    /// <summary>
    /// Submitted answer string
    /// </summary>
    [MaxLength(Limits.MaxFlagLength)]
    public string Answer { get; set; } = string.Empty;

    /// <summary>
    /// Status of the submitted answer
    /// </summary>
    public AnswerResult Status { get; set; } = AnswerResult.Accepted;

    /// <summary>
    /// Time the answer was submitted
    /// </summary>
    [JsonPropertyName("time")]
    public DateTimeOffset SubmitTimeUtc { get; set; } = DateTimeOffset.FromUnixTimeSeconds(0);

    /// <summary>
    /// User who submitted
    /// </summary>
    [JsonPropertyName("user")]
    public string UserName => User?.UserName ?? string.Empty;

    /// <summary>
    /// Team that submitted
    /// </summary>
    [JsonPropertyName("team")]
    public string TeamName => Team?.Name ?? string.Empty;

    /// <summary>
    /// Challenge that was submitted
    /// </summary>
    [JsonPropertyName("challenge")]
    public string ChallengeName => GameChallenge?.Title ?? string.Empty;

    #region Db Relationship

    /// <summary>
    /// User database ID
    /// </summary>
    [JsonIgnore]
    public Guid? UserId { get; set; }

    /// <summary>
    /// User
    /// </summary>
    [JsonIgnore]
    public UserInfo? User { get; set; }

    /// <summary>
    /// Participating team ID
    /// </summary>
    [JsonIgnore]
    public int TeamId { get; set; }

    /// <summary>
    /// Team
    /// </summary>
    [JsonIgnore]
    public Team? Team { get; set; }

    /// <summary>
    /// Participation ID
    /// </summary>
    [JsonIgnore]
    public int ParticipationId { get; set; }

    /// <summary>
    /// Team participation object
    /// </summary>
    [JsonIgnore]
    public Participation Participation { get; set; } = null!;

    /// <summary>
    /// Game ID
    /// </summary>
    [JsonIgnore]
    public int GameId { get; set; }

    /// <summary>
    /// Game
    /// </summary>
    [JsonIgnore]
    public Game? Game { get; set; } = null!;

    /// <summary>
    /// Challenge database ID
    /// </summary>
    [JsonIgnore]
    public int ChallengeId { get; set; }

    /// <summary>
    /// Challenge
    /// </summary>
    [JsonIgnore]
    public GameChallenge? GameChallenge { get; set; }

    #endregion Db Relationship
}
