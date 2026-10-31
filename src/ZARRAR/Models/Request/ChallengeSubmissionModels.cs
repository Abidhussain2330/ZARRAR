using System.ComponentModel.DataAnnotations;
using ZARRAR.Models.Data;

namespace ZARRAR.Models.Request;

public sealed class ChallengeSubmissionInfo
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public long PackageSize { get; set; }
    public string PackageHash { get; set; } = string.Empty;
    public DateTimeOffset SubmittedUtc { get; set; }
    public DateTimeOffset? ReviewedUtc { get; set; }
    public ChallengeSubmissionStatus Status { get; set; }
    public string ReviewNote { get; set; } = string.Empty;
    public Guid DeveloperId { get; set; }
    public string? DeveloperName { get; set; }

    public static ChallengeSubmissionInfo From(ChallengeSubmission item) => new()
    {
        Id = item.Id,
        Title = item.Title,
        Description = item.Description,
        PackageName = item.PackageName,
        PackageSize = item.PackageSize,
        PackageHash = item.PackageHash,
        SubmittedUtc = item.SubmittedUtc,
        ReviewedUtc = item.ReviewedUtc,
        Status = item.Status,
        ReviewNote = item.ReviewNote,
        DeveloperId = item.DeveloperId,
        DeveloperName = item.Developer?.UserName
    };
}

public sealed class ChallengeSubmissionReviewModel
{
    [Required]
    public ChallengeSubmissionStatus Status { get; set; }

    [MaxLength(2000)]
    public string? Note { get; set; }
}
