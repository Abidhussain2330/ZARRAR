using System.ComponentModel.DataAnnotations;

namespace ZARRAR.Models.Data;

public enum ChallengeSubmissionStatus : byte
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

/// <summary>
/// A challenge package submitted by an approved challenge developer.
/// The package is stored in ZARRAR blob storage and is never made public.
/// </summary>
public class ChallengeSubmission
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    [MaxLength(128)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string Description { get; set; } = string.Empty;

    [MaxLength(255)]
    public string PackageName { get; set; } = string.Empty;

    [MaxLength(64)]
    public string PackageHash { get; set; } = string.Empty;

    public long PackageSize { get; set; }
    public DateTimeOffset SubmittedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReviewedUtc { get; set; }
    public ChallengeSubmissionStatus Status { get; set; } = ChallengeSubmissionStatus.Pending;

    [MaxLength(2000)]
    public string ReviewNote { get; set; } = string.Empty;

    public Guid DeveloperId { get; set; }
    public UserInfo Developer { get; set; } = null!;
    public Guid? ReviewerId { get; set; }
}
