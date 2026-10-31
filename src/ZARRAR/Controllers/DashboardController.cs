using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using ZARRAR.Middlewares;
using ZARRAR.Models;
using ZARRAR.Models.Request.Dashboard;
using ZARRAR.Models.Request.Game;
using ZARRAR.Repositories.Interface;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace ZARRAR.Controllers;

/// <summary>
/// Command Centre Dashboard APIs
/// </summary>
/// <remarks>
/// Aggregates the data the participant dashboard needs into a handful of calls. Every figure is
/// read from the live database or from the same caches the rest of the platform uses; nothing here
/// is synthesised.
///
/// Visibility is resolved once per request, from the authenticated principal, into a single
/// <c>includeHidden</c> flag that is passed down to every query. Only <see cref="Role.Admin" />
/// sets that flag, so for every other caller — including anonymous ones — hidden games and
/// disabled challenges are excluded inside the SQL, not filtered out afterwards.
/// </remarks>
[ApiController]
[Route("api/[controller]")]
[Produces(MediaTypeNames.Application.Json)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
public class DashboardController(
    UserManager<UserInfo> userManager,
    IGameRepository gameRepository,
    IDashboardRepository dashboardRepository,
    IParticipationRepository participationRepository,
    IUserPreferencesRepository preferencesRepository,
    IStringLocalizer<Program> localizer) : ControllerBase
{
    /// <summary>
    /// Get platform-wide counters
    /// </summary>
    /// <remarks>
    /// Returns the counters shown at the head of the dashboard: active and upcoming games,
    /// challenges, participants and the number of solves in the last 24 hours. The aggregate is
    /// cached, so the figures are current as of the returned update time.
    ///
    /// This is the only dashboard endpoint open to anonymous callers, because it exposes nothing
    /// beyond totals over games that are already public. Hidden games and disabled challenges are
    /// excluded from every counter unless the caller is an administrator.
    /// </remarks>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved the platform counters</response>
    [HttpGet("overview")]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Query))]
    [ProducesResponseType(typeof(DashboardOverviewModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Overview(CancellationToken token)
    {
        // No user is required here, so the privilege is read from the request context rather than
        // from a resolved principal.
        var includeHidden = await ContextHelper.HasAdmin(HttpContext);

        var overview = await dashboardRepository.GetOverview(includeHidden, token);

        // The scope is part of the ETag: a cached privileged response must never satisfy a
        // conditional request made by an unprivileged caller.
        var eTag = $"\"dash-ov-{(includeHidden ? 'a' : 'p')}-{overview.UpdateTimeUtc.ToUnixTimeSeconds():X}\"";
        if (ContextHelper.IsNotModified(Request, Response, eTag, overview.UpdateTimeUtc, isPrivate: true))
            return StatusCode(StatusCodes.Status304NotModified);

        return Ok(overview);
    }

    /// <summary>
    /// Get the most relevant event for the caller
    /// </summary>
    /// <remarks>
    /// Returns an active game the caller has joined, preferring the one that ends soonest; when
    /// there is none, the game that starts soonest. Restricted to administrators: the live dashboard
    /// feeds (this one, live solves, category statistics and top participants) back the admin
    /// Command Center only and are not part of the player surface.
    /// </remarks>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved the featured event</response>
    /// <response code="404">No active or upcoming event is available</response>
    [RequireAdmin]
    [HttpGet("featured-event")]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Query))]
    [ProducesResponseType(typeof(FeaturedEventModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> FeaturedEvent(CancellationToken token)
    {
        var user = await userManager.GetUserAsync(User);

        if (user is null)
            return Unauthenticated();

        var includeHidden = CanViewHidden(user);

        var featuredId = await dashboardRepository.GetFeaturedGameId(user.Id, includeHidden, token);

        if (featuredId is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Dashboard_NoFeaturedEvent)],
                StatusCodes.Status404NotFound));

        var game = await dashboardRepository.GetVisibleGame(featuredId.Value, includeHidden, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Dashboard_NoFeaturedEvent)],
                StatusCodes.Status404NotFound));

        var teamCount = await participationRepository.GetParticipationCount(game.Id, token);
        var part = await participationRepository.GetParticipation(user.Id, game.Id, token);

        return Ok(FeaturedEventModel.FromGame(game, teamCount, part));
    }

    /// <summary>
    /// Get the recent solve feed
    /// </summary>
    /// <remarks>
    /// Returns the most recent accepted submissions across every game the caller can see. The feed
    /// carries no submitted answer and no submission identifier, and solves on disabled challenges
    /// or in hidden games never appear in it.
    ///
    /// The requested count is capped server side at
    /// <see cref="Limits.MaxDashboardLiveSolveCount" />, both by the model binder and again inside
    /// the repository.
    /// </remarks>
    /// <param name="count">Number of entries to return</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved the solve feed</response>
    [RequireAdmin]
    [HttpGet("live-solves")]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Query))]
    [ProducesResponseType(typeof(LiveSolveModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> LiveSolves(
        [FromQuery][Range(1, Limits.MaxDashboardLiveSolveCount)]
        int count = Limits.DefaultDashboardLiveSolveCount,
        CancellationToken token = default)
    {
        var user = await userManager.GetUserAsync(User);

        if (user is null)
            return Unauthenticated();

        return Ok(await dashboardRepository.GetLiveSolves(CanViewHidden(user), count, token));
    }

    /// <summary>
    /// Get per-category challenge and solve counts for a game
    /// </summary>
    /// <remarks>
    /// Grouped by the categories the game's challenges actually use; categories with no challenge
    /// are absent rather than reported as zero. Read from the cached scoreboard, which is built
    /// from enabled challenges only.
    /// </remarks>
    /// <param name="gameId">Game ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved the category statistics</response>
    /// <response code="400">The game has not started</response>
    /// <response code="404">Game not found</response>
    [RequireAdmin]
    [HttpGet("category-statistics")]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Query))]
    [ProducesResponseType(typeof(CategoryStatisticsModel[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CategoryStatistics(
        [FromQuery][Range(1, int.MaxValue)] int gameId,
        CancellationToken token = default)
    {
        var user = await userManager.GetUserAsync(User);

        if (user is null)
            return Unauthenticated();

        var game = await dashboardRepository.GetVisibleGame(gameId, CanViewHidden(user), token);

        // A hidden game is reported exactly as a missing one, so the response cannot be used to
        // tell the two apart.
        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        if (DateTimeOffset.UtcNow < game.StartTimeUtc)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_NotStarted)],
                ErrorCodes.GameNotStarted));

        var scoreboard = await gameRepository.GetScoreboard(game, token);

        var statistics = scoreboard.Challenges.Select(group =>
            {
                var challenges = group.Value.ToArray();
                return new CategoryStatisticsModel
                {
                    Category = group.Key,
                    ChallengeCount = challenges.Length,
                    SolvedCount = challenges.Sum(challenge => challenge.SolvedCount),
                    TotalScore = challenges.Sum(challenge => challenge.Score)
                };
            })
            .OrderBy(statistic => statistic.Category)
            .ToArray();

        return Ok(statistics);
    }

    /// <summary>
    /// Get the head of a game's leaderboard
    /// </summary>
    /// <remarks>
    /// Projected straight from the cached scoreboard, so the ranking matches the scoreboard page
    /// exactly; no score is recalculated here. Teams the scoreboard leaves unranked, because their
    /// division does not rank overall, are omitted.
    /// </remarks>
    /// <param name="gameId">Game ID</param>
    /// <param name="count">Number of entries to return</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved the leaderboard head</response>
    /// <response code="400">The game has not started</response>
    /// <response code="404">Game not found</response>
    [RequireAdmin]
    [HttpGet("top-participants")]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Query))]
    [ProducesResponseType(typeof(TopParticipantModel[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TopParticipants(
        [FromQuery][Range(1, int.MaxValue)] int gameId,
        [FromQuery][Range(1, Limits.MaxDashboardTopParticipantCount)]
        int count = Limits.DefaultDashboardTopParticipantCount,
        CancellationToken token = default)
    {
        var user = await userManager.GetUserAsync(User);

        if (user is null)
            return Unauthenticated();

        var game = await dashboardRepository.GetVisibleGame(gameId, CanViewHidden(user), token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        if (DateTimeOffset.UtcNow < game.StartTimeUtc)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_NotStarted)],
                ErrorCodes.GameNotStarted));

        var scoreboard = await gameRepository.GetScoreboard(game, token);

        // Clamped again here so the bound holds even if the binder is ever relaxed.
        var requested = Math.Clamp(count, 1, Limits.MaxDashboardTopParticipantCount);

        var leaders = scoreboard.Items.Values
            .Where(item => item.Rank > 0)
            .OrderBy(item => item.Rank)
            .Take(requested)
            .Select(TopParticipantModel.FromScoreboardItem)
            .ToArray();

        return Ok(leaders);
    }

    /// <summary>
    /// Get the caller's own standing
    /// </summary>
    /// <remarks>
    /// Returns the caller's rank, score, solve count and team for the given game, or for the
    /// featured game when no game is specified. A caller who has not joined — or for whom no
    /// relevant game exists — gets an unregistered shape with zeroed scoring fields rather than an
    /// error.
    /// </remarks>
    /// <param name="gameId">Game ID; when omitted the featured game is used</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved the caller's standing</response>
    /// <response code="404">The requested game was not found</response>
    [RequireUser]
    [HttpGet("current-user")]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Query))]
    [ProducesResponseType(typeof(DashboardUserStateModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CurrentUser(
        [FromQuery] int? gameId = null,
        CancellationToken token = default)
    {
        var user = await userManager.GetUserAsync(User);

        if (user is null)
            return Unauthenticated();

        var includeHidden = CanViewHidden(user);

        Game? game;

        if (gameId is { } requestedId)
        {
            game = await dashboardRepository.GetVisibleGame(requestedId, includeHidden, token);

            // An explicitly requested game that cannot be seen is reported as missing.
            if (game is null)
                return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                    StatusCodes.Status404NotFound));
        }
        else
        {
            var featuredId = await dashboardRepository.GetFeaturedGameId(user.Id, includeHidden, token);

            game = featuredId is null
                ? null
                : await dashboardRepository.GetVisibleGame(featuredId.Value, includeHidden, token);

            // Nothing to report on is a normal state for a platform between competitions.
            if (game is null)
                return Ok(new DashboardUserStateModel());
        }

        var state = new DashboardUserStateModel { GameId = game.Id, GameTitle = game.Title };

        var part = await participationRepository.GetParticipation(user.Id, game.Id, token);

        if (part is null)
            return Ok(state);

        state.Registered = true;
        state.Status = part.Status;
        state.TeamId = part.TeamId;
        state.TeamName = part.Team.Name;
        state.TeamAvatar = part.Team.AvatarUrl;
        state.DivisionId = part.DivisionId;

        // A rank only exists once the registration was accepted and the game has started; before
        // that there is no scoreboard to read one from.
        if (part.Status != ParticipationStatus.Accepted || DateTimeOffset.UtcNow < game.StartTimeUtc)
            return Ok(state);

        var scoreboard = await gameRepository.GetScoreboard(game, token);

        if (scoreboard.Items.TryGetValue(part.TeamId, out var item))
            state.WithScoreboardItem(item);

        return Ok(state);
    }

    /// <summary>
    /// Get the caller's interface preferences
    /// </summary>
    /// <remarks>
    /// A caller who has never saved anything receives the defaults; no row is written by this
    /// call.
    /// </remarks>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved the preferences</response>
    [RequireUser]
    [HttpGet("preferences")]
    [ProducesResponseType(typeof(UserPreferencesModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPreferences(CancellationToken token)
    {
        var user = await userManager.GetUserAsync(User);

        if (user is null)
            return Unauthenticated();

        var preferences = await preferencesRepository.GetPreferences(user.Id, token);

        return Ok(preferences is null
            ? UserPreferencesModel.Default
            : UserPreferencesModel.FromPreferences(preferences));
    }

    /// <summary>
    /// Update the caller's interface preferences
    /// </summary>
    /// <remarks>
    /// Writes the caller's own row and no other; the user the preferences are stored against comes
    /// from the authenticated principal and is never taken from the request body.
    /// </remarks>
    /// <param name="model">The preferences to store</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully stored the preferences</response>
    [RequireUser]
    [HttpPut("preferences")]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Concurrency))]
    [ProducesResponseType(typeof(UserPreferencesModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdatePreferences([FromBody] UserPreferencesModel model,
        CancellationToken token)
    {
        var user = await userManager.GetUserAsync(User);

        if (user is null)
            return Unauthenticated();

        var preferences = await preferencesRepository.SetPreferences(user.Id, model, token);

        return Ok(UserPreferencesModel.FromPreferences(preferences));
    }

    /// <summary>
    /// Whether the caller may see games that are marked hidden.
    /// </summary>
    /// <remarks>
    /// Hidden is an authoring state: a game is hidden while it is being prepared, and the platform
    /// only lets administrators edit games. Monitor deliberately does not qualify.
    /// </remarks>
    private static bool CanViewHidden(UserInfo user) => user.Role >= Role.Admin;

    /// <summary>
    /// The response for a principal that passed the privilege filter but could no longer be
    /// resolved, e.g. because the account was removed mid-request.
    /// </summary>
    private IActionResult Unauthenticated() =>
        Unauthorized(new RequestResponse(localizer[nameof(Resources.Program.Auth_LoginRequired)],
            StatusCodes.Status401Unauthorized));
}
