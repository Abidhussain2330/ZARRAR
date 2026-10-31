using System.Net.Mime;
using System.Text.Json;
using ZARRAR.Middlewares;
using ZARRAR.Models;
using ZARRAR.Models.Request.Admin;
using ZARRAR.Repositories.Interface;
using ZARRAR.Services.Config;
using ZARRAR.Services.Soc;
using ZARRAR.Services.Token;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace ZARRAR.Controllers;

/// <summary>
/// Controller for managing API tokens.
/// </summary>
/// <remarks>
/// Deliberately guarded by <see cref="RequireAdminAttribute" /> rather than
/// <see cref="RequireAdminOrTokenAttribute" />: there is no <c>TokenScope</c> for token
/// management, so an API token can never mint, re-scope, restore or revoke another token. Token
/// administration always requires an interactive administrator session.
/// </remarks>
[RequireAdmin]
[ApiController]
[Route("api/tokens")]
[Produces(MediaTypeNames.Application.Json)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
public class ApiTokenController(
    UserManager<UserInfo> userManager,
    IApiTokenRepository apiTokenRepository,
    ITokenService tokenService,
    IConfigService configService,
    ISocEventPublisher socEvents,
    ILogger<ApiTokenController> logger,
    IStringLocalizer<Program> localizer) : ControllerBase
{
    /// <summary>
    /// Generates a new API token.
    /// </summary>
    /// <remarks>
    /// The requested scope and expiry are both mandatory and both validated here as well as by
    /// model binding, so a token can never be issued unscoped or without a bounded lifetime.
    /// </remarks>
    /// <param name="model">The model for creating the token</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The generated token.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(ApiTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GenerateToken([FromBody] ApiTokenCreateModel model,
        CancellationToken cancellationToken = default)
    {
        // Reject an empty grant and any value carrying bits that map to no capability
        if (!model.Scope.IsValidGrant())
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.ApiToken_ScopeRequired)]));

        // Reject "never expires" and any lifetime beyond the platform bound
        if (model.ExpiresIn is not { } expiresIn || expiresIn == 0 ||
            expiresIn > Limits.MaxApiTokenLifetimeDays)
            return BadRequest(new RequestResponse(
                localizer[nameof(Resources.Program.ApiToken_ExpiryRequired), Limits.MaxApiTokenLifetimeDays]));

        var user = await userManager.GetUserAsync(User);

        // Step-up reauthentication. A minted token is a long-lived credential that survives logout
        // and password change, so a live administrator session must not be sufficient on its
        // own (finding F-022).
        if (await RequireCurrentPassword(user, model.Password) is { } stepUpFailure)
            return stepUpFailure;

        var createdAt = DateTimeOffset.UtcNow;

        var apiToken = new ApiToken
        {
            Name = model.Name,
            Scope = model.Scope,
            CreatedAt = createdAt,
            ExpiresAt = createdAt.AddDays(expiresIn),
            CreatorId = user?.Id,
            Creator = user
        };

        await apiTokenRepository.AddTokenAsync(apiToken, cancellationToken);
        var token = await tokenService.GenerateToken(apiToken, cancellationToken);
        await PublishTokenEventAsync(
            "ApiTokenCreated", SocSeverity.Medium,
            "An administrator created a bounded API token.", apiToken, false, cancellationToken);

        return Ok(new ApiTokenResponse(token, apiToken));
    }

    /// <summary>
    /// Lists all API tokens.
    /// </summary>
    /// <remarks>
    /// Each entry carries its <c>scope</c> and <c>expiresAt</c>, so an operator can spot tokens
    /// that still hold the full pre-scoping grant or the far-future expiry written by the
    /// <c>AddApiTokenScopeAndExpiry</c> migration.
    /// </remarks>
    /// <returns>A list of all API tokens.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(List<ApiToken>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListTokens(CancellationToken cancellationToken = default)
    {
        var tokens = await apiTokenRepository.GetTokensAsync(cancellationToken);
        return Ok(tokens);
    }

    /// <summary>
    /// Lists API tokens created by the current administrator.
    /// </summary>
    /// <remarks>
    /// This account-facing endpoint deliberately scopes the result using the authenticated
    /// principal. It avoids sending other operators' token metadata to a personal settings view;
    /// the existing global list remains available to administration workflows.
    /// </remarks>
    [HttpGet("mine")]
    [ProducesResponseType(typeof(List<ApiToken>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListOwnTokens(CancellationToken cancellationToken = default)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var tokens = await apiTokenRepository.GetTokensByCreatorAsync(user.Id, cancellationToken);
        return Ok(tokens);
    }

    /// <summary>
    /// Restores an API token.
    /// </summary>
    /// <param name="id">The ID of the token to restore.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    [HttpPost("{id:guid}/restore")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RestoreToken(Guid id, CancellationToken cancellationToken = default)
    {
        var token = await apiTokenRepository.GetTokenByIdAsync(id, cancellationToken);
        if (token == null)
            return NotFound();

        await apiTokenRepository.RestoreTokenAsync(token, cancellationToken);
        await PublishTokenEventAsync(
            "ApiTokenRestored", SocSeverity.Medium,
            "An administrator restored an API token.", token, false, cancellationToken);
        return Ok();
    }

    /// <summary>
    /// Revokes an API token.
    /// </summary>
    /// <param name="id">The ID of the token to revoke.</param>
    /// <param name="delete">If true, the token will be deleted instead of just revoked.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A success message if the token was revoked.</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeToken(Guid id, [FromQuery] bool delete = false,
        CancellationToken cancellationToken = default)
    {
        var token = await apiTokenRepository.GetTokenByIdAsync(id, cancellationToken);
        if (token == null)
            return NotFound();

        if (delete)
            await apiTokenRepository.DeleteTokenAsync(token, cancellationToken);
        else
            await apiTokenRepository.RevokeTokenAsync(token, cancellationToken);

        await PublishTokenEventAsync(
            delete ? "ApiTokenDeleted" : "ApiTokenRevoked",
            SocSeverity.Medium,
            delete ? "An administrator permanently deleted an API token." : "An administrator revoked an API token.",
            token, delete, cancellationToken);
        return Ok();
    }

    /// <summary>
    /// Revokes or permanently deletes one API token created by the current administrator.
    /// </summary>
    /// <remarks>
    /// A missing token and a token owned by another operator deliberately produce the same result.
    /// This keeps the account workflow creator-scoped without disclosing another token's existence.
    /// </remarks>
    [HttpDelete("mine/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeOwnToken(Guid id, [FromQuery] bool delete = false,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
            return Unauthorized();

        var token = await apiTokenRepository.GetTokenByIdAsync(id, cancellationToken);
        if (token?.CreatorId != user.Id)
            return NotFound();

        if (delete)
            await apiTokenRepository.DeleteTokenAsync(token, cancellationToken);
        else
            await apiTokenRepository.RevokeTokenAsync(token, cancellationToken);

        await PublishTokenEventAsync(
            delete ? "ApiTokenDeleted" : "ApiTokenRevoked",
            SocSeverity.Medium,
            delete ? "An administrator permanently deleted an API token." : "An administrator revoked an API token.",
            token, delete, cancellationToken);
        return Ok();
    }

    private Task<Guid> PublishTokenEventAsync(
        string eventType,
        SocSeverity severity,
        string details,
        ApiToken token,
        bool deleted,
        CancellationToken cancellationToken) =>
        socEvents.PublishAsync(new SocEventMessage(
            EventType: eventType,
            Category: SocCategory.Authentication,
            Severity: severity,
            Details: details,
            Source: nameof(ApiTokenController),
            PayloadJson: JsonSerializer.Serialize(new
            {
                tokenId = token.Id,
                scope = token.Scope.ToString(),
                token.ExpiresAt,
                deleted
            })), cancellationToken);

    /// <summary>
    /// Verify the caller's current password before a high-risk operation (finding F-022).
    /// </summary>
    /// <returns><see langword="null" /> on success, otherwise the failure result to return.</returns>
    async Task<IActionResult?> RequireCurrentPassword(UserInfo? user, string? encryptedPassword)
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
        await socEvents.PublishAsync(new SocEventMessage(
            EventType: "ApiTokenStepUpFailed",
            Category: SocCategory.Authentication,
            Severity: SocSeverity.Medium,
            Details: "Current-password verification failed before API-token creation.",
            Source: nameof(ApiTokenController),
            UserId: user.Id));

        return BadRequest(
            new RequestResponse(localizer[nameof(Resources.Program.Account_CurrentPasswordIncorrect)]));
    }
}
