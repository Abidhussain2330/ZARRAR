using System.ComponentModel.DataAnnotations;

namespace BURAQ.Models.Request.Account;

/// <summary>
/// Email change
/// </summary>
public class MailChangeModel
{
    /// <summary>
    /// New email
    /// </summary>
    [Required(ErrorMessageResourceName = nameof(Resources.Program.Model_EmailRequired),
        ErrorMessageResourceType = typeof(Resources.Program))]
    [EmailAddress(ErrorMessageResourceName = nameof(Resources.Program.Model_EmailMalformed),
        ErrorMessageResourceType = typeof(Resources.Program))]
    public string NewMail { get; set; } = string.Empty;

    /// <summary>
    /// Current password, required as step-up reauthentication before the recovery address is
    /// changed. Subject to API encryption like every other password field.
    /// </summary>
    [Required(ErrorMessageResourceName = nameof(Resources.Program.Account_CurrentPasswordRequired),
        ErrorMessageResourceType = typeof(Resources.Program))]
    [MaxLength(Limits.MaxEncryptedCredentialLength)]
    public string Password { get; set; } = string.Empty;
}
