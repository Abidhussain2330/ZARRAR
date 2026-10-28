using System.ComponentModel.DataAnnotations;
using BURAQ.Services;

namespace BURAQ.Models.Request.Account;

/// <summary>
/// Account recovery
/// </summary>
public class RecoveryModel : ModelWithCaptcha
{
    /// <summary>
    /// User email
    /// </summary>
    [Required(ErrorMessageResourceName = nameof(Resources.Program.Model_EmailRequired),
        ErrorMessageResourceType = typeof(Resources.Program))]
    [EmailAddress(ErrorMessageResourceName = nameof(Resources.Program.Model_EmailMalformed),
        ErrorMessageResourceType = typeof(Resources.Program))]
    public string? Email { get; set; }
}
