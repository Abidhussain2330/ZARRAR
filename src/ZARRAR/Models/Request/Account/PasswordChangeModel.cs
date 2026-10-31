using System.ComponentModel.DataAnnotations;

namespace ZARRAR.Models.Request.Account;

/// <summary>
/// Password change
/// </summary>
public class PasswordChangeModel
{
    /// <summary>
    /// Old password
    /// </summary>
    [Required(ErrorMessageResourceName = nameof(Resources.Program.Model_OldPasswordRequired),
        ErrorMessageResourceType = typeof(Resources.Program))]
    [MaxLength(Limits.MaxEncryptedCredentialLength)]
    public string Old { get; set; } = string.Empty;

    /// <summary>
    /// New password
    /// </summary>
    [Required(ErrorMessageResourceName = nameof(Resources.Program.Model_NewPasswordRequired),
        ErrorMessageResourceType = typeof(Resources.Program))]
    [MaxLength(Limits.MaxEncryptedCredentialLength)]
    public string New { get; set; } = string.Empty;
}
