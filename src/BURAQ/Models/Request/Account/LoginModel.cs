using System.ComponentModel.DataAnnotations;
using BURAQ.Services;

namespace BURAQ.Models.Request.Account;

/// <summary>
/// Login
/// </summary>
public class LoginModel : ModelWithCaptcha
{
    /// <summary>
    /// Username or email
    /// </summary>
    [Required]
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// Password
    /// </summary>
    [Required]
    [MaxLength(Limits.MaxEncryptedCredentialLength)]
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Two-step code (6-digit authenticator code, or a recovery code) for accounts that have
    /// two-step login turned on. Omit on the first attempt; the server asks for it.
    /// </summary>
    [MaxLength(32)]
    public string? TwoFactorCode { get; set; }
}

/// <summary>An authenticator or recovery code.</summary>
public class TwoFactorCodeModel
{
    [Required]
    [MaxLength(32)]
    public string Code { get; set; } = string.Empty;
}

/// <summary>Two-step login state for the signed-in account.</summary>
/// <param name="Enabled">Two-step login is on</param>
/// <param name="RecoveryCodesLeft">Unused recovery codes</param>
/// <param name="SharedKey">Authenticator key to add (only while setting up)</param>
/// <param name="AuthenticatorUri">otpauth:// URI for the same key (only while setting up)</param>
/// <param name="Recommended">Staff account (Monitor or Admin): two-step login is strongly recommended</param>
public record TwoFactorStatusModel(bool Enabled, int RecoveryCodesLeft, string? SharedKey, string? AuthenticatorUri,
    bool Recommended);

/// <summary>One-time recovery codes, shown once when two-step login is turned on.</summary>
public record TwoFactorRecoveryCodesModel(string[] RecoveryCodes);
