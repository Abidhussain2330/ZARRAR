using System.Net.Mime;
using BURAQ.Middlewares;
using BURAQ.Models;
using BURAQ.Models.Internal;
using BURAQ.Models.Request.Account;
using BURAQ.Repositories.Interface;
using BURAQ.Services;
using BURAQ.Services.Config;
using BURAQ.Services.Mail;
using BURAQ.Services.Soc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace BURAQ.Controllers;

/// <summary>
/// User account related APIs
/// </summary>
[ApiController]
[Route("api/[controller]/[action]")]
[Produces(MediaTypeNames.Application.Json)]
public class AccountController(
    IMailSender mailSender,
    IBlobRepository blobService,
    IHostEnvironment environment,
    ICaptchaService captcha,
    IConfigService configService,
    IOptionsSnapshot<AccountPolicy> accountPolicy,
    IOptionsSnapshot<GlobalConfig> globalConfig,
    UserManager<UserInfo> userManager,
    SignInManager<UserInfo> signInManager,
    PublicOriginResolver originResolver,
    ISocEventPublisher socEvents,
    AuthGuard authGuard,
    ILogger<AccountController> logger,
    IStringLocalizer<Program> localizer) : ControllerBase
{
    /// <summary>
    /// User registration
    /// </summary>
    /// <remarks>
    /// Use this API to register a new user. In development environment, no verification. Email URL: /verify
    /// </remarks>
    /// <param name="model"></param>
    /// <param name="token"></param>
    /// <response code="200">Registration successful</response>
    /// <response code="400">Validation failed or user already exists</response>
    [HttpPost]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Register))]
    [ProducesResponseType(typeof(RequestResponse<RegisterStatus>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] RegisterModel model, CancellationToken token = default)
    {
        if (!accountPolicy.Value.AllowRegister)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Account_RegisterNotEnabled)]));

        if (accountPolicy.Value.UseCaptcha && !await captcha.VerifyAsync(model, HttpContext, token))
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Account_TokenValidationFailed)]));

        if (!VerifyEmailDomain(model.Email))
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Account_AvailableEmailDomain),
                accountPolicy.Value.EmailDomainList]));

        var password = configService.DecryptApiData(model.Password);
        if (string.IsNullOrWhiteSpace(password))
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Model_PasswordRequired)]));
        if (password.Length > Limits.MaxPasswordLength)
            return BadRequest(new RequestResponse(
                localizer[nameof(Resources.Program.Model_PasswordTooLong), Limits.MaxPasswordLength]));

        var user = new UserInfo { UserName = model.UserName, Email = model.Email, Role = Role.User };

        user.UpdateByHttpContext(HttpContext);

        var result = await userManager.CreateAsync(user, password);

        if (!result.Succeeded)
        {
            var current = await userManager.FindByEmailAsync(model.Email);

            if (current is null)
                return HandleIdentityError(result.Errors);

            if (await userManager.IsEmailConfirmedAsync(current))
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Account_UserExisting)]));

            user = current;
        }
        else
        {
            await PublishAuthenticationEventAsync(
                "AccountRegistered", SocSeverity.Info,
                "A new user account was registered.", user.Id, token);
            // S2 — several fresh accounts from one address points at sock puppets.
            await authGuard.RecordRegistrationAsync(user.Id, ClientAddress(), token);
        }

        if (accountPolicy.Value.ActiveOnRegister)
        {
            user.EmailConfirmed = true;
            await userManager.UpdateAsync(user);
            await signInManager.SignInAsync(user, true);

            logger.Log(StaticLocalizer[nameof(Resources.Program.Account_UserRegisteredLog)], user,
                TaskStatus.Success);
            return Ok(new RequestResponse<RegisterStatus>(localizer[nameof(Resources.Program.Account_UserRegistered)],
                RegisterStatus.LoggedIn,
                StatusCodes.Status200OK));
        }

        if (!accountPolicy.Value.EmailConfirmationRequired)
        {
            logger.Log(StaticLocalizer[nameof(Resources.Program.Account_UserRegisteredWaitingApprovalLog)],
                user, TaskStatus.Success);
            return Ok(new RequestResponse<RegisterStatus>(
                localizer[nameof(Resources.Program.Account_UserRegisteredWaitingApproval)],
                RegisterStatus.AdminConfirmationRequired, StatusCodes.Status200OK));
        }

        logger.Log(StaticLocalizer[nameof(Resources.Program.Account_SendEmailVerification)], user,
            TaskStatus.Pending);

        var rToken = Codec.Base64.Encode(await userManager.GenerateEmailConfirmationTokenAsync(user));
        var link = GetEmailLink("verify", rToken, model.Email);

        if (link is null)
            return PublicOriginNotConfigured();

        if (environment.IsDevelopment())
        {
            logger.Log(link, user, TaskStatus.Pending, LogLevel.Debug);
        }
        else
        {
            if (!mailSender.SendConfirmEmailUrl(user.UserName, user.Email, link, localizer, globalConfig))
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Account_EmailSendFailed)]));
        }

        return Ok(new RequestResponse<RegisterStatus>(
            localizer[nameof(Resources.Program.Account_UserRegisteredWaitingEmailVerification)],
            RegisterStatus.EmailConfirmationRequired, StatusCodes.Status200OK));
    }

    private bool VerifyEmailDomain(string email)
    {
        var mailDomain = email.Split('@')[1];

        return string.IsNullOrWhiteSpace(accountPolicy.Value.EmailDomainList)
               || accountPolicy.Value.EmailDomainList
                   .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                   .Any(d => d.Equals(mailDomain, StringComparison.InvariantCulture));
    }

    /// <summary>
    /// User password recovery request
    /// </summary>
    /// <remarks>
    /// Use this API to request password recovery. Sends an email to the user. Email URL: /reset
    /// </remarks>
    /// <param name="model"></param>
    /// <param name="token"></param>
    /// <response code="200">Password reset email sent successfully</response>
    /// <response code="400">Validation failed</response>
    /// <response code="404">User does not exist</response>
    [HttpPost]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Register))]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Recovery([FromBody] RecoveryModel model, CancellationToken token = default)
    {
        if (accountPolicy.Value.UseCaptcha && !await captcha.VerifyAsync(model, HttpContext, token))
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Account_TokenValidationFailed)]));

        // This check reflects global policy, not the state of any individual account, so it is
        // identical for every caller and cannot be used to enumerate users.
        if (!accountPolicy.Value.EmailConfirmationRequired)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Account_ResetPasswordFromAdmin)]));

        logger.Log(StaticLocalizer[nameof(Resources.Program.Account_SendEmailVerification)], HttpContext,
            TaskStatus.Pending);

        // Every path below returns this same response. Recovery must not reveal whether an
        // address is registered, unconfirmed, banned or unknown (finding F-007) — outcomes are
        // recorded server-side instead.
        var genericResponse =
            Ok(new RequestResponse(localizer[nameof(Resources.Program.Account_EmailSent)], StatusCodes.Status200OK));

        var user = await userManager.FindByEmailAsync(model.Email!);
        await PublishAuthenticationEventAsync(
            "PasswordRecoveryRequested", SocSeverity.Low,
            "A password recovery request was submitted.", user?.Id, token);

        if (user is null || !user.EmailConfirmed)
        {
            logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.Account_RecoveryForUnknownAccount)],
                TaskStatus.Denied, LogLevel.Debug);
            return genericResponse;
        }

        var rToken = Codec.Base64.Encode(await userManager.GeneratePasswordResetTokenAsync(user));
        var link = GetEmailLink("reset", rToken, model.Email);

        // A missing canonical origin is a server misconfiguration, identical for all callers.
        if (link is null)
            return PublicOriginNotConfigured();

        if (environment.IsDevelopment())
        {
            logger.Log(link, user, TaskStatus.Pending, LogLevel.Debug);
        }
        else if (!mailSender.SendResetPasswordUrl(user.UserName, user.Email, link, localizer, globalConfig))
        {
            // Surface the delivery failure to operators only; reporting it to the caller would
            // re-introduce the enumeration oracle this endpoint is meant to close.
            logger.SystemLog(StaticLocalizer[nameof(Resources.Program.Account_EmailSendFailed)],
                TaskStatus.Failed, LogLevel.Warning);
        }

        return genericResponse;
    }

    /// <summary>
    /// User password reset
    /// </summary>
    /// <remarks>
    /// Use this API to reset the password. Email verification code is required.
    /// </remarks>
    /// <param name="model"></param>
    /// <response code="200">Password reset successfully</response>
    /// <response code="400">Validation failed</response>
    [HttpPost]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Register))]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PasswordReset([FromBody] PasswordResetModel model)
    {
        var password = configService.DecryptApiData(model.Password);
        if (string.IsNullOrWhiteSpace(password))
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Model_PasswordRequired)]));
        if (password.Length > Limits.MaxPasswordLength)
            return BadRequest(new RequestResponse(
                localizer[nameof(Resources.Program.Model_PasswordTooLong), Limits.MaxPasswordLength]));

        var user = await userManager.FindByEmailAsync(Codec.Base64.Decode(model.Email));
        if (user is null)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Account_InvalidEmail)]));

        user.UpdateByHttpContext(HttpContext);

        var token = Codec.Base64.Decode(model.RToken);
        var result = await userManager.ResetPasswordAsync(user, token, password);

        if (!result.Succeeded)
            return HandleIdentityError(result.Errors);

        logger.Log(StaticLocalizer[nameof(Resources.Program.Account_PasswordReset)], user, TaskStatus.Success);
        await PublishAuthenticationEventAsync(
            "PasswordReset", SocSeverity.Medium,
            "An account password was reset using a recovery token.", user.Id);

        return Ok();
    }

    /// <summary>
    /// User email confirmation
    /// </summary>
    /// <remarks>
    /// Use this API to confirm email using the verification code.
    /// </remarks>
    /// <param name="model"></param>
    /// <response code="200">Email verified successfully</response>
    /// <response code="400">Validation failed</response>
    /// <response code="401">Email verification failed</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Verify([FromBody] AccountVerifyModel model)
    {
        var user = await userManager.FindByEmailAsync(Codec.Base64.Decode(model.Email));

        if (user is null || user.EmailConfirmed)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Account_InvalidEmail)]));

        var result = await userManager.ConfirmEmailAsync(user, Codec.Base64.Decode(model.Token));

        if (!result.Succeeded)
            return Unauthorized(new RequestResponse(
                localizer[nameof(Resources.Program.Account_EmailVerificationFailed)],
                StatusCodes.Status401Unauthorized));

        logger.Log(StaticLocalizer[nameof(Resources.Program.Account_EmailVerified)], user, TaskStatus.Success);
        await signInManager.SignInAsync(user, true);

        user.LastSignedInUtc = DateTimeOffset.UtcNow;
        user.LastVisitedUtc = DateTimeOffset.UtcNow;
        user.RegisterTimeUtc = DateTimeOffset.UtcNow;

        result = await userManager.UpdateAsync(user);

        if (!result.Succeeded)
            return HandleIdentityError(result.Errors);

        await PublishAuthenticationEventAsync(
            "EmailVerified", SocSeverity.Info,
            "An account email address was verified.", user.Id);

        return Ok();
    }

    /// <summary>
    /// User login
    /// </summary>
    /// <remarks>
    /// Use this API to log in to the account.
    /// </remarks>
    /// <param name="model"></param>
    /// <param name="token"></param>
    /// <response code="200">Login successful</response>
    /// <response code="400">Validation failed</response>
    /// <response code="401">Incorrect username or password</response>
    [HttpPost]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Login))]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> LogIn([FromBody] LoginModel model, CancellationToken token = default)
    {
        if (accountPolicy.Value.UseCaptcha && !await captcha.VerifyAsync(model, HttpContext, token))
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Account_TokenValidationFailed)]));

        var password = configService.DecryptApiData(model.Password);
        if (string.IsNullOrWhiteSpace(password))
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Model_PasswordRequired)]));
        if (password.Length > Limits.MaxPasswordLength)
            return BadRequest(new RequestResponse(
                localizer[nameof(Resources.Program.Model_PasswordTooLong), Limits.MaxPasswordLength]));

        var user = await userManager.FindByNameAsync(model.UserName);
        user ??= await userManager.FindByEmailAsync(model.UserName);
        var address = ClientAddress();

        if (user is null)
        {
            await authGuard.RecordFailureAsync(null, model.UserName ?? string.Empty, address, token);
            await PublishAuthenticationEventAsync(
                "LoginFailed", SocSeverity.Low,
                "Login failed for an unknown account identifier.", token: token);
            return Unauthorized(new RequestResponse(
                localizer[nameof(Resources.Program.Account_IncorrectUserNameOrPassword)],
                StatusCodes.Status401Unauthorized));
        }

        if (user.Role == Role.Banned)
        {
            await PublishAuthenticationEventAsync(
                "LoginDeniedBannedAccount", SocSeverity.Medium,
                "Login was denied for a banned account.", user.Id, token);
            return Unauthorized(new RequestResponse(localizer[nameof(Resources.Program.Account_UserDisabled)],
                StatusCodes.Status401Unauthorized));
        }

        // Guessing is throttled per (account, source address) by AuthGuard rather than by
        // Identity's per-account lockout (finding F-009 still holds: guesses are bounded). A
        // per-account lockout would let anyone — e.g. the opponent in a 1v1, whose name is on the
        // projector — lock the real owner out; this way a guesser only locks out themselves.
        if (await authGuard.IsBlockedAsync(user.Id, address, token))
        {
            await PublishAuthenticationEventAsync(
                "LoginThrottled", SocSeverity.Medium,
                "A login attempt was refused because this address has too many recent failures for the account.",
                user.Id, token);

            // Same response as a wrong password: no confirmation that the account exists.
            return Unauthorized(new RequestResponse(
                localizer[nameof(Resources.Program.Account_IncorrectUserNameOrPassword)],
                StatusCodes.Status401Unauthorized));
        }

        await signInManager.SignOutAsync();

        // lockoutOnFailure: false — see above. An account an administrator has locked is still
        // refused, because sign-in always checks the lockout state.
        var result = await signInManager.PasswordSignInAsync(user, password, true, false);

        // S3 — two-step login. The password was right; the session is only issued once the
        // authenticator (or a one-time recovery) code checks out too.
        if (result.RequiresTwoFactor)
        {
            // PasswordSignInAsync left a short-lived "2FA pending" cookie; this endpoint finishes
            // the sign-in itself, so drop it either way.
            await HttpContext.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);

            var code = model.TwoFactorCode?.Replace(" ", string.Empty).Replace("-", string.Empty).Trim();
            if (string.IsNullOrEmpty(code))
                return Unauthorized(new RequestResponse(TwoFactorRequired, StatusCodes.Status401Unauthorized));

            var valid = await userManager.VerifyTwoFactorTokenAsync(user,
                            userManager.Options.Tokens.AuthenticatorTokenProvider, code) ||
                        (await userManager.RedeemTwoFactorRecoveryCodeAsync(user, model.TwoFactorCode!.Trim())).Succeeded;

            if (!valid)
            {
                await authGuard.RecordFailureAsync(user.Id, model.UserName ?? string.Empty, address, token);
                await PublishAuthenticationEventAsync(
                    "TwoFactorFailed", SocSeverity.Medium,
                    "The password was correct but the two-step code was wrong (possible stolen password).",
                    user.Id, token);
                return Unauthorized(new RequestResponse(TwoFactorInvalid, StatusCodes.Status401Unauthorized));
            }

            await signInManager.SignInAsync(user, isPersistent: true);
            result = Microsoft.AspNetCore.Identity.SignInResult.Success;
        }

        if (result.IsLockedOut)
        {
            logger.Log(StaticLocalizer[nameof(Resources.Program.Account_UserLockedOut)], user, TaskStatus.Denied,
                LogLevel.Warning);
            await PublishAuthenticationEventAsync(
                "AccountLockedOut", SocSeverity.High,
                "An account login was rejected because the lockout threshold was reached.", user.Id, token);

            // Deliberately the same response as a wrong password: reporting the lockout would
            // confirm the account exists and expose the lockout window to an attacker.
            return Unauthorized(new RequestResponse(
                localizer[nameof(Resources.Program.Account_IncorrectUserNameOrPassword)],
                StatusCodes.Status401Unauthorized));
        }

        if (!result.Succeeded)
        {
            await authGuard.RecordFailureAsync(user.Id, model.UserName ?? string.Empty, address, token);
            await PublishAuthenticationEventAsync(
                "LoginFailed", SocSeverity.Low,
                "Login failed for a known account.", user.Id, token);
            return Unauthorized(new RequestResponse(
                localizer[nameof(Resources.Program.Account_IncorrectUserNameOrPassword)],
                StatusCodes.Status401Unauthorized));
        }

        // Only a successful login updates the account's last sign-in and address. Updating them
        // before the password check let any failed attempt overwrite the real owner's address,
        // which also poisoned the SOC's shared-network signal.
        user.LastSignedInUtc = DateTimeOffset.UtcNow;
        user.UpdateByHttpContext(HttpContext);
        await userManager.UpdateAsync(user);
        await authGuard.RecordSuccessAsync(user.Id, address, token);

        logger.Log(StaticLocalizer[nameof(Resources.Program.Account_UserLogined)], user, TaskStatus.Success);
        await PublishAuthenticationEventAsync(
            "LoginSucceeded", SocSeverity.Info,
            "An interactive account login succeeded.", user.Id, token);

        // Staff without two-step login is the platform's softest target; make it visible.
        if (user.Role >= Role.Monitor && !user.TwoFactorEnabled)
            await PublishAuthenticationEventAsync(
                "PrivilegedLoginWithoutTwoFactor", SocSeverity.Low,
                "A Monitor or Admin account signed in without two-step login.", user.Id, token);

        return Ok();
    }

    /// <summary>Marker the login pages look for to show the two-step code field.</summary>
    private const string TwoFactorRequired = "Two-step verification code required.";
    private const string TwoFactorInvalid = "That two-step code is not valid.";

    /// <summary>
    /// Two-step login status, and a fresh authenticator key while it is off.
    /// </summary>
    [HttpGet]
    [RequireUser]
    [ProducesResponseType(typeof(TwoFactorStatusModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> TwoFactorStatus()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var recommended = user.Role >= Role.Monitor;
        if (user.TwoFactorEnabled)
            return Ok(new TwoFactorStatusModel(true, await userManager.CountRecoveryCodesAsync(user), null, null,
                recommended));

        var key = await userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            await userManager.ResetAuthenticatorKeyAsync(user);
            key = await userManager.GetAuthenticatorKeyAsync(user);
        }

        var issuer = Uri.EscapeDataString("ZARRAR");
        var label = Uri.EscapeDataString($"ZARRAR:{user.UserName}");
        var uri = $"otpauth://totp/{label}?secret={key}&issuer={issuer}&digits=6";
        return Ok(new TwoFactorStatusModel(false, 0, FormatKey(key ?? string.Empty), uri, recommended));
    }

    /// <summary>Turn two-step login on (confirms the authenticator with one code).</summary>
    [HttpPost]
    [RequireUser]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Login))]
    [ProducesResponseType(typeof(TwoFactorRecoveryCodesModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> TwoFactorEnable([FromBody] TwoFactorCodeModel model,
        CancellationToken token = default)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var code = model.Code.Replace(" ", string.Empty).Replace("-", string.Empty);
        if (!await userManager.VerifyTwoFactorTokenAsync(user, userManager.Options.Tokens.AuthenticatorTokenProvider,
                code))
            return BadRequest(new RequestResponse("That code doesn't match. Check the time on your phone and try again."));

        await userManager.SetTwoFactorEnabledAsync(user, true);
        var codes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        await PublishAuthenticationEventAsync("TwoFactorEnabled", SocSeverity.Info,
            "Two-step login was turned on for an account.", user.Id, token);

        return Ok(new TwoFactorRecoveryCodesModel(codes?.ToArray() ?? []));
    }

    /// <summary>Turn two-step login off (needs a current code or a recovery code).</summary>
    [HttpPost]
    [RequireUser]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Login))]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> TwoFactorDisable([FromBody] TwoFactorCodeModel model,
        CancellationToken token = default)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var code = model.Code.Replace(" ", string.Empty).Replace("-", string.Empty);
        var valid = await userManager.VerifyTwoFactorTokenAsync(user,
                        userManager.Options.Tokens.AuthenticatorTokenProvider, code) ||
                    (await userManager.RedeemTwoFactorRecoveryCodeAsync(user, model.Code.Trim())).Succeeded;
        if (!valid)
            return BadRequest(new RequestResponse(TwoFactorInvalid));

        await userManager.SetTwoFactorEnabledAsync(user, false);
        await userManager.ResetAuthenticatorKeyAsync(user);
        await PublishAuthenticationEventAsync("TwoFactorDisabled",
            user.Role >= Role.Monitor ? SocSeverity.High : SocSeverity.Medium,
            "Two-step login was turned off for an account.", user.Id, token);

        return Ok();
    }

    /// <summary>
    /// End every session of this account (other browsers within a minute, this one now).
    /// </summary>
    [HttpPost]
    [RequireUser]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> SignOutEverywhere(CancellationToken token = default)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        await userManager.UpdateSecurityStampAsync(user);
        await signInManager.SignOutAsync();
        await PublishAuthenticationEventAsync("SignedOutEverywhere", SocSeverity.Info,
            "An account ended all of its sessions.", user.Id, token);
        return Ok();
    }

    private static string FormatKey(string key)
    {
        var builder = new System.Text.StringBuilder();
        for (var i = 0; i < key.Length; i += 4)
        {
            if (builder.Length > 0)
                builder.Append(' ');
            builder.Append(key.AsSpan(i, Math.Min(4, key.Length - i)));
        }

        return builder.ToString().ToLowerInvariant();
    }

    /// <summary>
    /// User logout
    /// </summary>
    /// <remarks>
    /// Use this API to log out of the account. User permissions required.
    /// </remarks>
    /// <response code="200">Logged out successfully</response>
    /// <response code="401">Unauthorized</response>
    [HttpPost]
    [RequireUser]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> LogOut()
    {
        var user = await userManager.GetUserAsync(User);
        await signInManager.SignOutAsync();
        await PublishAuthenticationEventAsync(
            "LogoutSucceeded", SocSeverity.Info,
            "An interactive account session logged out.", user?.Id);

        return Ok();
    }

    /// <summary>
    /// User data update
    /// </summary>
    /// <remarks>
    /// Use this API to update username and description. User permissions required.
    /// </remarks>
    /// <param name="model"></param>
    /// <response code="200">User data updated successfully</response>
    /// <response code="400">Validation failed or user data update failed</response>
    /// <response code="401">Unauthorized</response>
    [HttpPut]
    [RequireUser]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update([FromBody] ProfileUpdateModel model)
    {
        var user = await userManager.GetUserAsync(User);

        if (model.UserName is not null && model.UserName != user!.UserName)
        {
            var oldName = user.UserName;

            var unameRes = await userManager.SetUserNameAsync(user, model.UserName);

            if (!unameRes.Succeeded)
                return HandleIdentityError(unameRes.Errors);

            logger.Log(StaticLocalizer[nameof(Resources.Program.Account_UserUpdated), oldName!, user.UserName!],
                user, TaskStatus.Success);
        }

        user!.UpdateUserInfo(model);
        var result = await userManager.UpdateAsync(user);

        if (!result.Succeeded)
            return HandleIdentityError(result.Errors);

        await PublishAuthenticationEventAsync(
            "ProfileUpdated", SocSeverity.Info,
            "An account profile was updated.", user.Id);

        return Ok();
    }

    /// <summary>
    /// User password change
    /// </summary>
    /// <remarks>
    /// Use this API to change user's password. User permissions required.
    /// </remarks>
    /// <param name="model"></param>
    /// <response code="200">Password changed successfully</response>
    /// <response code="400">Validation failed or password change failed</response>
    /// <response code="401">Unauthorized</response>
    [HttpPut]
    [RequireUser]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangePassword([FromBody] PasswordChangeModel model)
    {
        var user = await userManager.GetUserAsync(User);

        var oldPassword = configService.DecryptApiData(model.Old);
        if (string.IsNullOrWhiteSpace(oldPassword))
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Model_OldPasswordRequired)]));

        var newPassword = configService.DecryptApiData(model.New);
        if (string.IsNullOrWhiteSpace(newPassword))
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Model_NewPasswordRequired)]));
        if (oldPassword.Length > Limits.MaxPasswordLength || newPassword.Length > Limits.MaxPasswordLength)
            return BadRequest(new RequestResponse(
                localizer[nameof(Resources.Program.Model_PasswordTooLong), Limits.MaxPasswordLength]));

        var result = await userManager.ChangePasswordAsync(user!, oldPassword, newPassword);

        if (!result.Succeeded)
            return HandleIdentityError(result.Errors);

        logger.Log(StaticLocalizer[nameof(Resources.Program.Account_PasswordChanged)], user,
            TaskStatus.Success);
        await PublishAuthenticationEventAsync(
            "PasswordChanged", SocSeverity.Medium,
            "An authenticated account changed its password.", user!.Id);

        return Ok();
    }

    /// <summary>
    /// User email change
    /// </summary>
    /// <remarks>
    /// Use this API to change user's email. User permissions required. Email URL: /confirm
    /// </remarks>
    /// <param name="model"></param>
    /// <response code="200">Email change email sent successfully. Boolean indicates whether email verification is required</response>
    /// <response code="400">Validation failed or email already in use</response>
    /// <response code="401">Unauthorized</response>
    [HttpPut]
    [RequireUser]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Register))]
    [ProducesResponseType(typeof(RequestResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangeEmail([FromBody] MailChangeModel model)
    {
        var user = await userManager.GetUserAsync(User);

        // Step-up reauthentication. Redirecting the recovery address is an account-takeover
        // primitive, so possession of a live session must not be sufficient on its own (F-022).
        if (await RequireCurrentPassword(user, model.Password) is { } stepUpFailure)
            return stepUpFailure;

        if (await userManager.FindByEmailAsync(model.NewMail) is not null)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Account_EmailUsed)]));

        if (!VerifyEmailDomain(model.NewMail))
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Account_AvailableEmailDomain),
                accountPolicy.Value.EmailDomainList]));

        if (!accountPolicy.Value.EmailConfirmationRequired)
            return BadRequest(
                new RequestResponse<bool>(localizer[nameof(Resources.Program.Account_ChangeEmailFromAdmin)], false));

        logger.Log(StaticLocalizer[nameof(Resources.Program.Account_SendEmailChange)], user,
            TaskStatus.Pending);

        var token = Codec.Base64.Encode(await userManager.GenerateChangeEmailTokenAsync(user!, model.NewMail));
        var link = GetEmailLink("confirm", token, model.NewMail);

        if (link is null)
            return PublicOriginNotConfigured();

        if (environment.IsDevelopment())
        {
            logger.Log(link, user, TaskStatus.Pending, LogLevel.Debug);
        }
        else
        {
            if (!mailSender.SendChangeEmailUrl(user!.UserName, model.NewMail, link, localizer, globalConfig))
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Account_EmailSendFailed)]));
        }

        await PublishAuthenticationEventAsync(
            "EmailChangeRequested", SocSeverity.Low,
            "An authenticated account requested an email-address change.", user!.Id);

        return Ok(new RequestResponse<bool>(localizer[nameof(Resources.Program.Account_EmailVerificationPending)], true,
            StatusCodes.Status200OK));
    }

    /// <summary>
    /// User email change confirmation
    /// </summary>
    /// <remarks>
    /// Use this API to confirm email change. Email verification code required. User permissions required.
    /// </remarks>
    /// <param name="model"></param>
    /// <response code="200">Email changed successfully</response>
    /// <response code="400">Validation failed or invalid email</response>
    /// <response code="401">Unauthorized</response>
    /// <response code="403">Forbidden</response>
    [HttpPost]
    [RequireUser]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> MailChangeConfirm([FromBody] AccountVerifyModel model)
    {
        var user = await userManager.GetUserAsync(User);
        var result = await userManager.ChangeEmailAsync(user!, Codec.Base64.Decode(model.Email),
            Codec.Base64.Decode(model.Token));

        if (!result.Succeeded)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Account_InvalidEmail)]));

        logger.Log(StaticLocalizer[nameof(Resources.Program.Account_EmailChanged)], user, TaskStatus.Success);
        await PublishAuthenticationEventAsync(
            "EmailChanged", SocSeverity.Medium,
            "An authenticated account changed its email address.", user!.Id);

        return Ok();
    }

    /// <summary>
    /// Get user information
    /// </summary>
    /// <remarks>
    /// Use this API to get user information. User permissions required.
    /// </remarks>
    /// <response code="200">User information retrieved successfully</response>
    /// <response code="401">Unauthorized</response>
    [HttpGet]
    [RequireUser]
    [ProducesResponseType(typeof(ProfileUserInfoModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Profile()
    {
        var user = await userManager.GetUserAsync(User);

        return Ok(ProfileUserInfoModel.FromUserInfo(user!));
    }

    /// <summary>
    /// Update user avatar
    /// </summary>
    /// <remarks>
    /// Use this API to update user's avatar. User permissions required.
    /// </remarks>
    /// <response code="200">User avatar URL</response>
    /// <response code="400">Invalid request</response>
    /// <response code="401">Unauthorized</response>
    [HttpPut]
    [RequireUser]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Avatar(IFormFile file, CancellationToken token)
    {
        switch (file.Length)
        {
            case 0:
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.File_SizeZero)]));
            case > 3 * 1024 * 1024:
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.File_SizeTooLarge)]));
        }

        var user = await userManager.GetUserAsync(User);

        if (user!.AvatarHash is not null)
            await blobService.DeleteBlobByHash(user.AvatarHash, token);

        var avatar = await blobService.CreateOrUpdateImage(file, "avatar", 300, token);

        if (avatar is null)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Avatar_UpdateFailed)]));

        user.AvatarHash = avatar.Hash;
        var result = await userManager.UpdateAsync(user);

        if (result != IdentityResult.Success)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Account_UserUpdateFailed)]));

        logger.Log(StaticLocalizer[nameof(Resources.Program.Account_AvatarUpdated), avatar.Hash[..8]], user,
            TaskStatus.Success);

        return Ok(avatar.Url());
    }

    /// <summary>
    /// Remove user avatar
    /// </summary>
    /// <remarks>
    /// Removes the authenticated user's current avatar. User permissions required.
    /// </remarks>
    /// <response code="200">Avatar removed or no avatar was configured</response>
    /// <response code="401">Unauthorized</response>
    [HttpDelete("/api/account/avatar")]
    [RequireUser]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RemoveAvatar(CancellationToken token)
    {
        var user = await userManager.GetUserAsync(User);

        if (user!.AvatarHash is null)
            return Ok();

        var avatarHash = user.AvatarHash;
        user.AvatarHash = null;

        var result = await userManager.UpdateAsync(user);

        if (result != IdentityResult.Success)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Account_UserUpdateFailed)]));

        await blobService.DeleteBlobByHash(avatarHash, token);
        logger.LogInformation("Account avatar removed for user {UserId}", user.Id);

        return Ok();
    }

    /// <summary>
    /// Build an absolute account link from the canonical public origin.
    /// </summary>
    /// <remarks>
    /// These links carry a live single-use token, so the origin must never come from the
    /// client-supplied Host header — an edge that forwards an arbitrary Host would otherwise let
    /// an attacker redirect a victim's reset token to a domain they control (finding F-001).
    /// Returns null when no trustworthy origin is available; callers must then refuse to send.
    /// </remarks>
    private string? GetEmailLink(string action, string token, string? email)
    {
        var origin = originResolver.Resolve(globalConfig.Value, HttpContext.Request);

        if (origin is null)
            return null;

        return $"{origin}/account/{action}?token={token}&email={Codec.Base64.Encode(email)}";
    }

    /// <summary>
    /// Response used when no trustworthy public origin is configured.
    /// </summary>
    /// <remarks>
    /// Identical for every caller, so it cannot be used to infer anything about an account.
    /// </remarks>
    private ObjectResult PublicOriginNotConfigured() =>
        StatusCode(StatusCodes.Status500InternalServerError,
            new RequestResponse(localizer[nameof(Resources.Program.Config_PublicUrlNotConfigured)],
                StatusCodes.Status500InternalServerError));

    /// <summary>
    /// Require proof of the current password before a high-impact account operation.
    /// </summary>
    /// <param name="user">The signed-in user.</param>
    /// <param name="encryptedPassword">API-encrypted current password supplied by the client.</param>
    /// <returns>An error result to return, or null when verification succeeded.</returns>
    private async Task<IActionResult?> RequireCurrentPassword(UserInfo? user, string? encryptedPassword)
    {
        if (user is null)
            return Unauthorized(new RequestResponse(
                localizer[nameof(Resources.Program.Account_IncorrectUserNameOrPassword)],
                StatusCodes.Status401Unauthorized));

        if (string.IsNullOrWhiteSpace(encryptedPassword))
            return BadRequest(
                new RequestResponse(localizer[nameof(Resources.Program.Account_CurrentPasswordRequired)]));

        var password = configService.DecryptApiData(encryptedPassword);

        if (string.IsNullOrWhiteSpace(password))
            return BadRequest(
                new RequestResponse(localizer[nameof(Resources.Program.Account_CurrentPasswordRequired)]));

        if (password.Length > Limits.MaxPasswordLength)
            return BadRequest(new RequestResponse(
                localizer[nameof(Resources.Program.Model_PasswordTooLong), Limits.MaxPasswordLength]));

        if (await userManager.CheckPasswordAsync(user, password))
            return null;

        logger.Log(StaticLocalizer[nameof(Resources.Program.Account_StepUpFailed)], user, TaskStatus.Denied,
            LogLevel.Warning);
        await PublishAuthenticationEventAsync(
            "StepUpAuthenticationFailed", SocSeverity.Medium,
            "Current-password verification failed for a high-impact account operation.", user.Id);

        return BadRequest(
            new RequestResponse(localizer[nameof(Resources.Program.Account_CurrentPasswordIncorrect)]));
    }

    private Task<Guid> PublishAuthenticationEventAsync(
        string eventType,
        SocSeverity severity,
        string details,
        Guid? userId = null,
        CancellationToken token = default) =>
        socEvents.PublishAsync(new SocEventMessage(
            EventType: eventType,
            Category: SocCategory.Authentication,
            Severity: severity,
            Details: details,
            Source: nameof(AccountController),
            UserId: userId,
            // Where the attempt came from: without it the SOC cannot tell one guesser from many.
            PayloadJson: System.Text.Json.JsonSerializer.Serialize(new
            {
                address = ClientAddress(),
                userAgent = Truncate(Request.Headers.UserAgent.ToString(), 200)
            })), token);

    /// <summary>The client's address (forwarded headers are applied for the trusted proxy).</summary>
    private string ClientAddress() => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];

    private BadRequestObjectResult HandleIdentityError(IEnumerable<IdentityError> errors) =>
        BadRequest(new RequestResponse(errors.FirstOrDefault()?.Description ??
                                       localizer[nameof(Resources.Program.Identity_UnknownError)]));
}
