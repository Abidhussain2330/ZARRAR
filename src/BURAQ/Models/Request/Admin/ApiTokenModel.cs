using System.ComponentModel.DataAnnotations;

namespace BURAQ.Models.Request.Admin;

/// <summary>
/// API token creation model.
/// </summary>
public class ApiTokenCreateModel
{
    /// <summary>
    /// The user-friendly name for the token to identify its purpose.
    /// </summary>
    [Required]
    [MaxLength(128)]
    public required string Name { get; set; }

    /// <summary>
    /// The capabilities granted to the token.
    /// </summary>
    /// <remarks>
    /// A bit flag combination of <see cref="TokenScope" />. Must be non-empty: an unscoped token
    /// used to be admin-equivalent (finding F-012), so the caller has to state what the token may
    /// do. <see cref="TokenScope.All" /> is still accepted but should be reserved for replacing a
    /// pre-scoping token.
    /// </remarks>
    [Required]
    public TokenScope Scope { get; set; } = TokenScope.None;

    /// <summary>
    /// The duration for which the token will be valid, in days.
    /// </summary>
    /// <remarks>
    /// Mandatory and bounded by <see cref="Limits.MaxApiTokenLifetimeDays" />. A token can no
    /// longer be issued without an expiry.
    /// </remarks>
    [Required]
    [Range(1, Limits.MaxApiTokenLifetimeDays)]
    public uint? ExpiresIn { get; set; }

    /// <summary>
    /// The administrator's current password.
    /// </summary>
    /// <remarks>
    /// Step-up reauthentication (finding F-022). Minting a token creates a long-lived credential
    /// that survives logout and password change, so possession of a live administrator session must
    /// not be sufficient on its own. Subject to API encryption like every other password field.
    /// </remarks>
    [Required(ErrorMessageResourceName = nameof(Resources.Program.Account_CurrentPasswordRequired),
        ErrorMessageResourceType = typeof(Resources.Program))]
    [MaxLength(Limits.MaxEncryptedCredentialLength)]
    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// This record represents the response for an API token request.
/// </summary>
public record ApiTokenResponse(string Token, ApiToken Info);
