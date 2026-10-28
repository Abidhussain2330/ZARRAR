using System.ComponentModel.DataAnnotations;

namespace BURAQ.Models.Request.Admin;

/// <summary>
/// Team creation request made by an administrator.
/// </summary>
public sealed class AdminTeamCreateModel
{
    /// <summary>
    /// Team name.
    /// </summary>
    [Required(ErrorMessageResourceName = nameof(Resources.Program.Team_NameEmpty),
        ErrorMessageResourceType = typeof(Resources.Program))]
    [MaxLength(Limits.MaxTeamNameLength, ErrorMessageResourceName = nameof(Resources.Program.Model_TeamNameTooLong),
        ErrorMessageResourceType = typeof(Resources.Program))]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional team bio.
    /// </summary>
    [MaxLength(Limits.MaxTeamBioLength, ErrorMessageResourceName = nameof(Resources.Program.Model_TeamBioTooLong),
        ErrorMessageResourceType = typeof(Resources.Program))]
    public string? Bio { get; set; }

    /// <summary>
    /// Existing user who becomes the team captain and first member.
    /// </summary>
    [Required]
    public Guid? CaptainId { get; set; }
}
