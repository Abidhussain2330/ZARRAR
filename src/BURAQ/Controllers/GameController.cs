using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.Mime;
using System.Security.Claims;
using System.Threading.Channels;
using BURAQ.Middlewares;
using BURAQ.Models;
using BURAQ.Models.Internal;
using BURAQ.Models.Request.Admin;
using BURAQ.Models.Request.Game;
using BURAQ.Repositories.Interface;
using BURAQ.Services.Cache;
using BURAQ.Services.Config;
using BURAQ.Services.Soc;
using BURAQ.Services.Zarrar;
using BURAQ.Storage.Interface;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace BURAQ.Controllers;

/// <summary>
/// Game related APIs
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces(MediaTypeNames.Application.Json)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
public class GameController(
    ILogger<GameController> logger,
    UserManager<UserInfo> userManager,
    ChannelWriter<Submission> channelWriter,
    CacheHelper cacheHelper,
    IBlobStorage storage,
    IConfigService configService,
    IBlobRepository blobService,
    IGameRepository gameRepository,
    ITeamRepository teamRepository,
    IDivisionRepository divisionRepository,
    IGameEventRepository eventRepository,
    IGameNoticeRepository noticeRepository,
    ICheatInfoRepository cheatInfoRepository,
    IContainerRepository containerRepository,
    IGameEventRepository gameEventRepository,
    ISubmissionRepository submissionRepository,
    IGameChallengeRepository challengeRepository,
    IGameInstanceRepository gameInstanceRepository,
    IParticipationRepository participationRepository,
    IOptionsSnapshot<ContainerPolicy> containerPolicy,
    IConfiguration configuration,
    AppDbContext db,
    ISocEventPublisher socEvents,
    ZarrarDuelService zarrar,
    IStringLocalizer<Program> localizer) : ControllerBase
{
    private readonly byte[] _fingerprintKey = configuration["XorKey"]?.ToUTF8Bytes() ?? [];

    /// <summary>
    /// Get the recent games
    /// </summary>
    /// <remarks>
    /// Retrieves recent game in three weeks
    /// </remarks>
    /// <param name="limit">Limit of the number of games</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved game information</response>
    [HttpGet("Recent")]
    [ProducesResponseType(typeof(BasicGameInfoModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> RecentGames(
        [FromQuery][Range(0, 50)] int limit,
        CancellationToken token)
    {
        (var games, var lastModified) = await gameRepository.GetRecentGames(token);
        var eTag = $"\"{lastModified.ToUnixTimeSeconds():X}-{limit}\"";
        if (ContextHelper.IsNotModified(Request, Response, eTag, lastModified))
            return StatusCode(StatusCodes.Status304NotModified);

        return Ok(limit > 0 ? games.Take(limit) : games);
    }

    /// <summary>
    /// Get games
    /// </summary>
    /// <remarks>
    /// Retrieves game information in specified range
    /// </remarks>
    /// <param name="count"></param>
    /// <param name="skip"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved game notices</response>
    /// <response code="400">Game not found</response>
    [HttpGet]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Query))]
    [ResponseCache(VaryByQueryKeys = ["count", "skip"], Duration = 60)]
    [ProducesResponseType(typeof(ArrayResponse<BasicGameInfoModel>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Games([FromQuery][Range(0, 50)] int count = 10,
        [FromQuery] int skip = 0, CancellationToken token = default)
        => Ok(await gameRepository.GetGameInfo(count, skip, token));

    /// <summary>
    /// Get detailed game information
    /// </summary>
    /// <remarks>
    /// Retrieves detailed information about the game
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved game information</response>
    /// <response code="404">Game not found</response>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(DetailedGameInfoModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Game(int id, CancellationToken token)
    {
        var gameInfo = await gameRepository.GetDetailedGameInfo(id, token);

        if (gameInfo is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        var count = await participationRepository.GetParticipationCount(id, token);

        Participation? part = null;
        if (await userManager.GetUserAsync(User) is { } user)
            part = await participationRepository.GetParticipation(user.Id, id, token);

        return Ok(gameInfo.WithParticipation(part, count));
    }

    /// <summary>
    /// Get check info for joining a game
    /// </summary>
    /// <param name="id"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    [RequireUser]
    [HttpGet("{id:int}/Check")]
    [ProducesResponseType(typeof(GameJoinCheckInfoModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetGameJoinCheckInfo(int id, CancellationToken token)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        var user = await userManager.GetUserAsync(User);

        return Ok(await gameRepository.GetCheckInfo(game, user!, token));
    }

    /// <summary>
    /// Join a game
    /// </summary>
    /// <remarks>
    /// Join a game; requires User permission
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="model"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully joined the game</response>
    /// <response code="403">Unauthorized operation or invalid operation</response>
    /// <response code="404">Game not found</response>
    [RequireUser]
    [HttpPost("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> JoinGame(int id, [FromBody] GameJoinModel model, CancellationToken token)
    {
        await using var transaction = await gameRepository.BeginTransactionAsync(token);
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        if (!game.PracticeMode && game.EndTimeUtc < DateTimeOffset.UtcNow)
            return BadRequest(
                new RequestResponse(localizer[nameof(Resources.Program.Game_Ended)], ErrorCodes.GameEnded));

        var user = await userManager.GetUserAsync(User);
        var team = await teamRepository.GetTeamById(model.TeamId, token);

        if (team is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Team_NotFound)],
                StatusCodes.Status404NotFound));

        if (team.Members.All(u => u.Id != user!.Id))
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_NotMemberOfTeam)]));

        // =============== Validate division and permissions ===============

        var joinableDivisionIds = await divisionRepository.GetJoinableDivisionIds(id, token);

        Division? div = null;
        if (joinableDivisionIds is { Count: > 0 })
        {
            // We don't allow joining a game with joinable divisions without specifying a division
            if (model.DivisionId is null)
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_DivisionRequired)]));

            // Division must allow joining
            if (!joinableDivisionIds.Contains(model.DivisionId.Value))
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_InvalidDivision)]));

            div = await divisionRepository.GetDivision(id, model.DivisionId.Value, token);
            if (div is null)
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_InvalidDivision)]));

            // just redundant check, it takes little cost
            if (!div.DefaultPermissions.HasFlag(GamePermission.JoinGame))
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_InvalidDivision)]));
        }

        // =============== Validate invitation code ===============

        string? requiredInviteCode;
        if (div is not null)
            requiredInviteCode = string.IsNullOrEmpty(div.InviteCode) ? null : div.InviteCode;
        else
            requiredInviteCode = string.IsNullOrEmpty(game.InviteCode) ? null : game.InviteCode;

        if (requiredInviteCode is not null && requiredInviteCode != model.InviteCode)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_InvalidInvitationCode)]));

        // =============== Check and handle participation state ===============

        // Get existing participation for this team in this game
        var part = await participationRepository.GetParticipation(team, game, token);

        // Check if user is already in this game through a different team (exclude rejected participations)
        if (await participationRepository.CheckRepeatParticipation(user!, game, token))
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_InOtherTeam)]));

        // Always clean up rejected user participations in this game
        await participationRepository.RemoveUserParticipations(user!, game, token);

        if (part is null)
        {
            // Create new participation
            part = new() { Game = game, Team = team, Division = div, Token = gameRepository.GetToken(game, team) };
            participationRepository.Add(part);
        }
        else if (part.Status == ParticipationStatus.Rejected)
        {
            // Allow changing division when re-joining after rejection
            part.Division = div;
            part.Status = ParticipationStatus.Pending;
        }
        else if (part.DivisionId != model.DivisionId)
        {
            // If trying to change division, reject
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_InvalidDivision)]));
        }

        // =============== Verify team member count limit ===============

        if (game.TeamMemberCountLimit > 0 && part.Members.Count >= game.TeamMemberCountLimit)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_TeamMemberLimitExceeded)]));

        // =============== All checks passed, add user to the team ===============

        part.Members.Add(new(user!, game, team));

        await participationRepository.SaveAsync(token);

        var shouldAcceptWithoutReview = div is null
            ? game.AcceptWithoutReview
            : !div.DefaultPermissions.HasFlag(GamePermission.RequireReview);
        if (shouldAcceptWithoutReview)
            await participationRepository.UpdateParticipationStatus(part, ParticipationStatus.Accepted, token);

        await transaction.CommitAsync(token);
        await cacheHelper.FlushScoreboardCache(part.GameId, token);

        logger.Log(StaticLocalizer[nameof(Resources.Program.Game_JoinSucceeded), team.Name, game.Title], user,
            TaskStatus.Success);

        return Ok();
    }

    /// <summary>
    /// Leave a game
    /// </summary>
    /// <remarks>
    /// Leave a game; requires User permission
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully left the game</response>
    /// <response code="403">Unauthorized operation or invalid operation</response>
    /// <response code="404">Game not found</response>
    [RequireUser]
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> LeaveGame(int id, CancellationToken token)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        var user = await userManager.GetUserAsync(User);

        var part = await participationRepository.GetParticipation(user!.Id, game.Id, token);

        if (part is null || part.Members.All(u => u.UserId != user.Id))
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_CannotLeaveWithoutJoin)]));

        if (part.Status != ParticipationStatus.Pending && part.Status != ParticipationStatus.Rejected)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_CannotLeaveAfterApproval)]));

        part.Members.RemoveWhere(u => u.UserId == user.Id);

        if (part.Members.Count == 0)
            await participationRepository.RemoveParticipation(part, true, token);
        else
            await participationRepository.SaveAsync(token);

        return Ok();
    }

    /// <summary>
    /// Get the scoreboard
    /// </summary>
    /// <remarks>
    /// Retrieves the scoreboard data
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved game information</response>
    /// <response code="400">Game not found</response>
    [HttpGet("{id:int}/Scoreboard")]
    [ProducesResponseType(typeof(ScoreboardModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Scoreboard([FromRoute] int id, CancellationToken token)
    {
        var scoreboard = await gameRepository.TryGetScoreboard(id, token);
        string eTag;
        if (scoreboard is not null)
        {
            eTag = GameETag(id, scoreboard.UpdateTimeUtc);
            if (ContextHelper.IsNotModified(Request, Response, eTag, scoreboard.UpdateTimeUtc))
                return StatusCode(StatusCodes.Status304NotModified);

            return Ok(scoreboard);
        }

        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        if (DateTimeOffset.UtcNow < game.StartTimeUtc)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_NotStarted)]));

        scoreboard = await gameRepository.GetScoreboard(game, token);
        var lastModified = scoreboard.UpdateTimeUtc;
        eTag = GameETag(game.Id, lastModified);
        ContextHelper.SetCacheHeaders(Response, eTag, lastModified);

        return Ok(scoreboard);
    }

    /// <summary>
    /// Get the leaderboard
    /// </summary>
    /// <remarks>
    /// Returns a trimmed, ranked slice of the scoreboard for the leaderboard page: the podium and
    /// the visible table rows, plus the caller's own team when it falls outside that slice.
    ///
    /// This exists alongside <see cref="Scoreboard" /> rather than replacing it. The full endpoint
    /// returns every team, every timeline, every division and every challenge in one document —
    /// correct for the detailed scoreboard, but far more than the leaderboard renders. Both read
    /// the same cached ScoreboardModel, so this adds no database work; what it saves is response
    /// size and client-side JSON parsing.
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="count">Number of ranked entries to return (1-100)</param>
    /// <param name="divisionId">Optional division filter</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved leaderboard</response>
    /// <response code="400">Game has not started</response>
    /// <response code="404">Game not found</response>
    [HttpGet("{id:int}/Leaderboard")]
    [ProducesResponseType(typeof(LeaderboardModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Leaderboard([FromRoute] int id, [FromQuery] int count = 20,
        [FromQuery] int? divisionId = null, CancellationToken token = default)
    {
        // Clamped rather than trusted: count reaches a Take() below, so an unbounded value would
        // let a caller request the entire ranking through an endpoint meant to be the cheap one.
        count = Math.Clamp(count, 1, 100);

        var result = await GetCachedScoreboard(id, token);
        if (result.Error is not null)
            return result.Error;

        var scoreboard = result.Scoreboard!;

        var ranked = scoreboard.Items.Values
            .Where(item => divisionId is null || item.DivisionId == divisionId)
            .OrderBy(item => divisionId is null ? item.Rank : item.DivisionRank ?? item.Rank)
            .ToArray();

        var entries = ranked.Take(count).Select(LeaderboardEntryModel.FromScoreboardItem).ToArray();

        // Only resolved for signed-in callers, and only when their team isn't already in the slice —
        // otherwise the client would render the same team twice.
        LeaderboardEntryModel? currentTeam = null;
        if (User.Identity?.IsAuthenticated is true)
        {
            var user = await userManager.GetUserAsync(User);
            if (user is not null)
            {
                var participation = await participationRepository.GetParticipation(user.Id, id, token);
                if (participation is not null && entries.All(e => e.Id != participation.TeamId))
                {
                    var own = ranked.FirstOrDefault(item => item.Id == participation.TeamId);
                    if (own is not null)
                        currentTeam = LeaderboardEntryModel.FromScoreboardItem(own);
                }
            }
        }

        return Ok(new LeaderboardModel
        {
            Entries = entries,
            CurrentTeam = currentTeam,
            TotalCount = ranked.Length,
            UpdateTimeUtc = scoreboard.UpdateTimeUtc
        });
    }

    /// <summary>
    /// Get the leaderboard score timeline
    /// </summary>
    /// <remarks>
    /// Returns only the per-team score series used to draw the progression graph.
    ///
    /// Split out from the leaderboard payload on purpose: the graph is behind a disclosure that
    /// most visitors never open, and the series are the largest part of the scoreboard document.
    /// Keeping them here means the leaderboard's first paint doesn't pay for a chart nobody asked
    /// to see yet.
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="count">Number of teams to include (1-20)</param>
    /// <param name="divisionId">Optional division filter</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved timeline</response>
    /// <response code="400">Game has not started</response>
    /// <response code="404">Game not found</response>
    [HttpGet("{id:int}/Leaderboard/Timeline")]
    [ProducesResponseType(typeof(IEnumerable<TopTimeLine>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> LeaderboardTimeline([FromRoute] int id, [FromQuery] int count = 10,
        [FromQuery] int? divisionId = null, CancellationToken token = default)
    {
        count = Math.Clamp(count, 1, 20);

        var result = await GetCachedScoreboard(id, token);
        if (result.Error is not null)
            return result.Error;

        var scoreboard = result.Scoreboard!;

        // TimeLines is keyed by division; 0 holds the game-wide series.
        var key = divisionId ?? 0;
        if (!scoreboard.TimeLines.TryGetValue(key, out var timeLines))
            timeLines = scoreboard.TimeLines.TryGetValue(0, out var fallback) ? fallback : [];

        return Ok(timeLines.Take(count));
    }

    /// <summary>
    /// Resolves the scoreboard from cache, falling back to a rebuild, and applies the standard
    /// ETag / 304 handling. Shared by the leaderboard endpoints so their caching behaviour cannot
    /// drift apart from each other.
    /// </summary>
    private async Task<(ScoreboardModel? Scoreboard, IActionResult? Error)> GetCachedScoreboard(int id,
        CancellationToken token)
    {
        var scoreboard = await gameRepository.TryGetScoreboard(id, token);
        string eTag;

        if (scoreboard is not null)
        {
            eTag = GameETag(id, scoreboard.UpdateTimeUtc);
            if (ContextHelper.IsNotModified(Request, Response, eTag, scoreboard.UpdateTimeUtc))
                return (null, StatusCode(StatusCodes.Status304NotModified));

            return (scoreboard, null);
        }

        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return (null, NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound)));

        if (DateTimeOffset.UtcNow < game.StartTimeUtc)
            return (null, BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_NotStarted)])));

        scoreboard = await gameRepository.GetScoreboard(game, token);
        eTag = GameETag(game.Id, scoreboard.UpdateTimeUtc);
        ContextHelper.SetCacheHeaders(Response, eTag, scoreboard.UpdateTimeUtc);

        return (scoreboard, null);
    }

    /// <summary>
    /// Get game notices
    /// </summary>
    /// <remarks>
    /// Retrieves game notice data
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="count"></param>
    /// <param name="skip"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved game notices</response>
    /// <response code="400">Game not found</response>
    [HttpGet("{id:int}/Notices")]
    [ProducesResponseType(typeof(GameNotice[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Notices([FromRoute] int id, [FromQuery][Range(0, 100)] int count = 100,
        [FromQuery][Range(0, 300)] int skip = 0, CancellationToken token = default)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        if (DateTimeOffset.UtcNow < game.StartTimeUtc)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_NotStarted)]));

        (var data, var lastModified) = await noticeRepository.GetLatestNotices(game.Id, token);
        var eTag = $"\"{game.Id}-{lastModified.ToUnixTimeSeconds():X}-{skip}-{count}\"";
        if (ContextHelper.IsNotModified(Request, Response, eTag, lastModified))
            return StatusCode(StatusCodes.Status304NotModified);
        return Ok(data.Skip(skip).Take(count));
    }

    /// <summary>
    /// Get game events
    /// </summary>
    /// <remarks>
    /// Retrieves game event data; requires Monitor permission
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="count"></param>
    /// <param name="hideContainer">Hide container events</param>
    /// <param name="skip"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved game events</response>
    /// <response code="400">Game not found</response>
    [RequireMonitor]
    [HttpGet("{id:int}/Events")]
    [ProducesResponseType(typeof(GameEventInfoModel[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Events([FromRoute] int id, [FromQuery] bool hideContainer = false,
        [FromQuery][Range(0, 100)] int count = 100, [FromQuery] int skip = 0, CancellationToken token = default)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        if (DateTimeOffset.UtcNow < game.StartTimeUtc)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_NotStarted)]));

        return Ok((await eventRepository.GetEvents(game.Id, hideContainer, count, skip, token))
            .Select(gameEvent => GameEventInfoModel.FromGameEvent(gameEvent, _fingerprintKey)));
    }

    /// <summary>
    /// Get game submissions
    /// </summary>
    /// <remarks>
    /// Retrieves game submission data; requires Monitor permission
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="type">Submission type</param>
    /// <param name="count"></param>
    /// <param name="skip"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved game submissions</response>
    /// <response code="400">Game not found</response>
    [RequireMonitor]
    [HttpGet("{id:int}/Submissions")]
    [ProducesResponseType(typeof(SubmissionInfoModel[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Submissions([FromRoute] int id, [FromQuery] AnswerResult? type = null,
        [FromQuery][Range(0, 100)] int count = 100, [FromQuery] int skip = 0, CancellationToken token = default)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        if (DateTimeOffset.UtcNow < game.StartTimeUtc)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_NotStarted)]));

        return Ok((await submissionRepository.GetSubmissions(game, type, count, skip, token))
            .Select(submission => SubmissionInfoModel.FromSubmission(submission, _fingerprintKey)));
    }

    /// <summary>
    /// Get game cheat information
    /// </summary>
    /// <remarks>
    /// Retrieves game cheat data; requires Monitor permission
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved game cheat data</response>
    /// <response code="400">Game not found</response>
    [RequireMonitor]
    [HttpGet("{id:int}/CheatInfo")]
    [ProducesResponseType(typeof(CheatInfoModel[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CheatInfo([FromRoute] int id, CancellationToken token = default)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound));

        if (DateTimeOffset.UtcNow < game.StartTimeUtc)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_NotStarted)]));

        return Ok((await cheatInfoRepository.GetCheatInfoByGameId(game.Id, token))
            .Select(info => CheatInfoModel.FromCheatInfo(info, _fingerprintKey)));
    }

    /// <summary>
    /// Get challenges with traffic capturing enabled
    /// </summary>
    /// <remarks>
    /// Retrieves challenges with traffic capturing enabled; requires Monitor permission
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved challenge list</response>
    /// <response code="404">Capture information not found</response>
    [RequireMonitor]
    [HttpGet("Games/{id:int}/Captures")]
    [ProducesResponseType(typeof(ChallengeTrafficModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetChallengesWithTrafficCapturing([FromRoute] int id, CancellationToken token)
    {
        var challenges = await challengeRepository.GetChallengesWithTrafficCapturing(id, token);

        var results = await Task.WhenAll(
            challenges.Select(c => ChallengeTrafficModel.FromChallengeAsync(c, storage, token))
        );

        return Ok(results);
    }

    /// <summary>
    /// Get team captures in a challenge
    /// </summary>
    /// <remarks>
    /// Retrieves the list of captured teams for a game challenge; requires Monitor permission
    /// </remarks>
    /// <param name="challengeId">Challenge ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved file list</response>
    /// <response code="404">Capture information not found</response>
    [RequireMonitor]
    [HttpGet("Captures/{challengeId:int}")]
    [ProducesResponseType(typeof(TeamTrafficModel[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetChallengeTraffic([FromRoute] int challengeId, CancellationToken token)
    {
        var path = StoragePath.Combine(PathHelper.Capture, $"{challengeId}");

        var entries = await storage.ListAsync(path, cancellationToken: token);
        var participationIds = entries.Select(e => int.TryParse(e.Name, out var id) ? id : -1)
            .Where(id => id > 0).ToArray();

        if (participationIds.Length == 0)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_CaptureNotFound)],
                StatusCodes.Status404NotFound));

        var participation = await participationRepository.GetParticipationsByIds(participationIds, token);

        var results = await Task.WhenAll(
            participation.Select(p => TeamTrafficModel.FromParticipationAsync(p, challengeId, storage, token))
        );

        return Ok(results);
    }

    /// <summary>
    /// Get traffic files
    /// </summary>
    /// <remarks>
    /// Retrieves traffic packet files for a team and challenge; requires Monitor permission
    /// </remarks>
    /// <param name="challengeId">Challenge ID</param>
    /// <param name="partId">Team participation ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved file list</response>
    /// <response code="404">Capture information not found</response>
    [RequireMonitor]
    [HttpGet("Captures/{challengeId:int}/{partId:int}")]
    [ProducesResponseType(typeof(FileRecord[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTeamTraffic([FromRoute] int challengeId, [FromRoute] int partId,
        CancellationToken token)
    {
        var path = StoragePath.Combine(PathHelper.Capture, $"{challengeId}", $"{partId}");

        var blobs = await storage.ListAsync(path, cancellationToken: token);

        var results = blobs.Select(blob => new FileRecord
        {
            FileName = blob.Name,
            Size = blob.Size ?? 0,
            UpdateTime = blob.LastModificationTime ?? DateTimeOffset.MinValue
        }).ToArray();

        return Ok(results);
    }

    /// <summary>
    /// Download all traffic files
    /// </summary>
    /// <remarks>
    /// Downloads all traffic packet files for a team and challenge; requires Monitor permission
    /// </remarks>
    /// <param name="challengeId">Challenge ID</param>
    /// <param name="partId">Team participation ID</param>
    /// <param name="token">Token</param>
    /// <response code="200">Successfully retrieved files</response>
    /// <response code="404">Capture information not found</response>
    [RequireMonitor]
    [HttpGet("Captures/{challengeId:int}/{partId:int}/All")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetAllTeamTraffic([FromRoute] int challengeId, [FromRoute] int partId,
        CancellationToken token)
    {
        var path = StoragePath.Combine(PathHelper.Capture, $"{challengeId}", $"{partId}");

        var filename = $"Capture-{challengeId}-{partId}-{DateTimeOffset.UtcNow:yyyyMMdd-HH.mm.ssZ}";

        return new TarDirectoryResult(storage, path, filename, token);
    }

    /// <summary>
    /// Deletes all traffic files
    /// </summary>
    /// <remarks>
    /// Deletes a team's traffic packet files for a challenge; requires Monitor permission
    /// </remarks>
    /// <param name="challengeId">Challenge ID</param>
    /// <param name="partId">Team participation ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully deleted files</response>
    /// <response code="404">Capture information not found</response>
    [RequireMonitor]
    [HttpDelete("Captures/{challengeId:int}/{partId:int}/All")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteAllTeamTraffic([FromRoute] int challengeId, [FromRoute] int partId,
        CancellationToken token)
    {
        try
        {
            var path = StoragePath.Combine(PathHelper.Capture, $"{challengeId}", $"{partId}");

            await storage.DeleteAsync(path, token);

            return Ok();
        }
        catch (Exception e)
        {
            return BadRequest(new RequestResponse(e.Message));
        }
    }

    /// <summary>
    /// Get a traffic file
    /// </summary>
    /// <remarks>
    /// Retrieves a traffic packet file; requires Monitor permission
    /// </remarks>
    /// <param name="challengeId">Challenge ID</param>
    /// <param name="partId">Team participation ID</param>
    /// <param name="filename">Traffic packet filename</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved file</response>
    /// <response code="404">Capture information not found</response>
    [RequireMonitor]
    [HttpGet("Captures/{challengeId:int}/{partId:int}/{filename}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTeamTraffic([FromRoute] int challengeId, [FromRoute] int partId,
        [FromRoute] string filename, CancellationToken token)
    {
        try
        {
            var path = StoragePath.Combine(PathHelper.Capture, $"{challengeId}", $"{partId}", filename);

            if (!await storage.ExistsAsync(path, token))
                return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_CaptureNotFound)]));

            var stream = await storage.OpenReadAsync(path, token);

            return File(stream, MediaTypeNames.Application.Octet, filename);
        }
        catch
        {
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_CaptureNotFound)]));
        }
    }

    /// <summary>
    /// Deletes a traffic file
    /// </summary>
    /// <remarks>
    /// Deletes a traffic packet file; requires Monitor permission
    /// </remarks>
    /// <param name="challengeId">Challenge ID</param>
    /// <param name="partId">Team participation ID</param>
    /// <param name="filename">Traffic packet filename</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully deleted file</response>
    /// <response code="404">Capture information not found</response>
    [RequireMonitor]
    [HttpDelete("Captures/{challengeId:int}/{partId:int}/{filename}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteTeamTraffic([FromRoute] int challengeId, [FromRoute] int partId,
        [FromRoute] string filename, CancellationToken token)
    {
        try
        {
            var path = StoragePath.Combine(PathHelper.Capture, $"{challengeId}", $"{partId}", filename);

            if (!await storage.ExistsAsync(path, token))
                return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_CaptureNotFound)]));

            await storage.DeleteAsync(path, token);

            return Ok();
        }
        catch
        {
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_CaptureNotFound)]));
        }
    }

    /// <summary>
    /// Get team details in a game
    /// </summary>
    /// <remarks>
    /// Retrieves all challenges of the game; requires User permission and active team participation
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved game challenge information</response>
    /// <response code="400">Invalid operation</response>
    /// <response code="404">Game not found</response>
    [RequireUser]
    [HttpGet("{id:int}/Details")]
    [ProducesResponseType(typeof(GameDetailModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChallengesWithTeamInfo([FromRoute] int id, CancellationToken token)
    {
        var gameClosed = await gameRepository.IsGameClosed(id, token);
        if (gameClosed)
            return BadRequest(
                new RequestResponse(localizer[nameof(Resources.Program.Game_Ended)], ErrorCodes.GameEnded));

        var scoreboard = await gameRepository.TryGetScoreboard(id, token);
        string eTag;
        if (scoreboard is not null)
        {
            eTag = GameETag(id, scoreboard.UpdateTimeUtc);
            if (ContextHelper.IsNotModified(Request, Response, eTag, scoreboard.UpdateTimeUtc, true))
                return StatusCode(StatusCodes.Status304NotModified);
        }

        var context = await GetContextInfo(id, token: token);

        if (context.Result is not null)
            return context.Result;

        scoreboard ??= await gameRepository.GetScoreboard(context.Game!, token);
        var lastModified = scoreboard.UpdateTimeUtc;
        eTag = GameETag(context.Game!.Id, lastModified);
        ContextHelper.SetCacheHeaders(Response, eTag, lastModified, true);

        var challenges = scoreboard.Challenges;
        if (context.Participation!.DivisionId is { } divId &&
            scoreboard.Divisions.TryGetValue(divId, out var division))
        {
            // filter out challenges is can be viewed by division permission
            challenges = FilterChallengesByPermission(scoreboard.Challenges, division);
        }

        // ZARRAR: a captured challenge is gone for both players (its points still count).
        if (ZarrarDuelService.IsDuelGame(id))
            challenges = ZarrarDuelService.HideCaptured(challenges);

        var boardItem = scoreboard.Items.TryGetValue(context.Participation!.TeamId, out var item)
            ? item
            : new()
            {
                Avatar = context.Participation!.Team.AvatarUrl,
                Rank = 0,
                Name = context.Participation!.Team.Name,
                Id = context.Participation!.TeamId
            };

        return Ok(new GameDetailModel
        {
            ScoreboardItem = boardItem,
            TeamToken = context.Participation!.Token,
            Challenges = challenges,
            ChallengeCount = challenges.Count,
            WriteupRequired = context.Game!.WriteupRequired,
            WriteupDeadline = context.Game!.WriteupDeadline
        });
    }

    /// <summary>
    /// Get all game participations
    /// </summary>
    /// <remarks>
    /// Retrieves all participation information of the game; requires Admin permission
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved game participation information</response>
    /// <response code="400">Invalid operation</response>
    /// <response code="404">Game not found</response>
    [RequireAdmin]
    [HttpGet("{id:int}/Participations")]
    [ProducesResponseType(typeof(ParticipationInfoModel[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Participations([FromRoute] int id, CancellationToken token = default)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)]));

        return Ok((await participationRepository.GetParticipations(game, token))
            .Select(ParticipationInfoModel.FromParticipation));
    }

    /// <summary>
    /// Downloads the scoreboard
    /// </summary>
    /// <remarks>
    /// Downloads the game scoreboard; requires Monitor permission
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="excelHelper"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully downloaded game scoreboard</response>
    /// <response code="400">Invalid operation</response>
    /// <response code="404">Game not found</response>
    [RequireMonitor]
    [HttpGet("{id:int}/ScoreboardSheet")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [SuppressMessage("ReSharper", "StringLiteralTypo")]
    public async Task<IActionResult> ScoreboardSheet([FromRoute] int id, [FromServices] ExcelHelper excelHelper,
        CancellationToken token = default)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)]));

        if (DateTimeOffset.UtcNow < game.StartTimeUtc)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_NotStarted)]));

        try
        {
            var scoreboard = await gameRepository.GetScoreboardWithMembers(game, token);
            var stream = excelHelper.GetScoreboardExcel(scoreboard);
            stream.Seek(0, SeekOrigin.Begin);

            return File(stream,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"{game.Title}-Scoreboard-{DateTimeOffset.Now:yyyyMMdd-HH.mm.ssZ}.xlsx");
        }
        catch (Exception ex)
        {
            logger.SystemLog(StaticLocalizer[nameof(Resources.Program.Game_ScoreboardDownloadFailed)],
                TaskStatus.Failed, LogLevel.Error);
            logger.LogErrorMessage(ex, ex.Message);
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_ScoreboardDownloadFailed)]));
        }
    }

    /// <summary>
    /// Downloads all submissions
    /// </summary>
    /// <remarks>
    /// Downloads all submissions of the game; requires Monitor permission
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="excelHelper"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully downloaded all game submissions</response>
    /// <response code="400">Invalid operation</response>
    /// <response code="404">Game not found</response>
    [RequireMonitor]
    [HttpGet("{id:int}/SubmissionSheet")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [SuppressMessage("ReSharper", "StringLiteralTypo")]
    public async Task<IActionResult> SubmissionSheet([FromRoute] int id, [FromServices] ExcelHelper excelHelper,
        CancellationToken token = default)
    {
        var game = await gameRepository.GetGameById(id, token);

        if (game is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)]));

        if (DateTimeOffset.UtcNow < game.StartTimeUtc)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_NotStarted)]));

        var submissions = await submissionRepository.GetSubmissions(game, count: 0, token: token);

        var stream = excelHelper.GetSubmissionExcel(submissions);
        stream.Seek(0, SeekOrigin.Begin);

        return File(stream,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"{game.Title}_Submissions_{DateTimeOffset.Now:yyyyMMddHHmmss}.xlsx");
    }

    /// <summary>
    /// Get challenge information
    /// </summary>
    /// <remarks>
    /// Retrieves challenge information; requires User permission and active team participation
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="challengeId">Challenge ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved game challenge information</response>
    /// <response code="400">Invalid operation</response>
    /// <response code="404">Game not found</response>
    [RequireUser]
    [HttpGet("{id:int}/Challenges/{challengeId:int}")]
    [ProducesResponseType(typeof(ChallengeDetailModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetChallenge([FromRoute] int id, [FromRoute] int challengeId,
        CancellationToken token)
    {
        if (id <= 0 || challengeId <= 0)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Challenge_NotFound)],
                StatusCodes.Status404NotFound));

        var context = await GetContextInfo(id, token: token);

        if (context.Result is not null)
            return context.Result;

        if (await ZarrarGuard(id, context.Participation!, challengeId, false, token) is { } zarrarResult)
            return zarrarResult;

        var permission = await divisionRepository.GetPermission(context.Participation?.DivisionId, challengeId, token);

        if (!permission.HasFlag(GamePermission.ViewChallenge))
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Challenge_NotFound)],
                StatusCodes.Status404NotFound));

        var instance = await gameInstanceRepository.GetInstance(context.Participation!, challengeId, token);

        if (instance is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_ChallengeNotFound)],
                StatusCodes.Status404NotFound));

        var scoreboard = await gameRepository.GetScoreboard(context.Game!, token);
        var scoreboardChallenge =
            scoreboard.ChallengeMap.TryGetValue(challengeId, out var challenge) ? challenge : null;

        var attempts = await submissionRepository.CountSubmissions(context.Participation!.Id, challengeId, token);

        var unlockedHintPenalties = await db.HintUnlocks
            .AsNoTracking()
            .Where(unlock => unlock.ParticipationId == context.Participation.Id &&
                             unlock.ChallengeId == challengeId)
            .ToDictionaryAsync(unlock => unlock.HintIndex, unlock => unlock.Penalty, token);

        var detail = ChallengeDetailModel.FromInstance(instance, attempts, scoreboardChallenge, unlockedHintPenalties);
        if (await db.SharedRuntimeConfigurations.AsNoTracking().AnyAsync(x =>
                x.ChallengeId == challengeId && x.IsEnabled, token))
            detail.Context.Url = SharedRuntimeRoutes.ForGame(id, challengeId);

        return Ok(detail);
    }

    /// <summary>
    /// Unlocks a paid hint for the caller's team.
    /// </summary>
    /// <remarks>
    /// A successful unlock is shared with every member of the same team participation. The operation is idempotent;
    /// repeat requests never apply the deduction twice.
    /// </remarks>
    [RequireUser]
    [HttpPost("{id:int}/Challenges/{challengeId:int}/Hints/{hintIndex:int}")]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Query))]
    [ProducesResponseType(typeof(ChallengeHintDetailModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> UnlockHint([FromRoute] int id, [FromRoute] int challengeId,
        [FromRoute] int hintIndex, CancellationToken token)
    {
        if (hintIndex < 0)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Challenge_NotFound)],
                StatusCodes.Status404NotFound));

        var context = await GetContextInfo(id, token: token);
        if (context.Result is not null)
            return context.Result;

        var participation = context.Participation!;
        var user = context.User!;

        var permission = await divisionRepository.GetPermission(participation.DivisionId, challengeId, token);
        if (!permission.HasFlag(GamePermission.ViewChallenge))
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Challenge_NotFound)],
                StatusCodes.Status404NotFound));

        var instance = await gameInstanceRepository.GetInstance(participation, challengeId, token);
        var hints = instance?.Challenge.Hints;
        if (instance is null || hints is null || hintIndex >= hints.Count)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_ChallengeNotFound)],
                StatusCodes.Status404NotFound));

        var penalty = instance.Challenge.GetHintPenalty(hintIndex);
        var alreadyUnlocked = penalty == 0 || await db.HintUnlocks
            .AsNoTracking()
            .AnyAsync(unlock => unlock.ParticipationId == participation.Id &&
                                unlock.ChallengeId == challengeId &&
                                unlock.HintIndex == hintIndex, token);

        if (!alreadyUnlocked && await db.FirstSolves
                .AsNoTracking()
                .AnyAsync(solve => solve.ParticipationId == participation.Id && solve.ChallengeId == challengeId, token))
            return BadRequest(new RequestResponse("Hints cannot be unlocked after this challenge has been solved."));

        if (penalty > 0)
        {
            var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO \"HintUnlocks\" (\"ParticipationId\", \"ChallengeId\", \"HintIndex\", \"Penalty\", \"UnlockedAtUtc\", \"UserId\")
                VALUES ({participation.Id}, {challengeId}, {hintIndex}, {penalty}, {DateTimeOffset.UtcNow}, {user.Id})
                ON CONFLICT (\"ParticipationId\", \"ChallengeId\", \"HintIndex\") DO NOTHING
                """, token);

            if (affected > 0)
            {
                await cacheHelper.FlushScoreboardCache(id, token);
                socEvents.Enqueue(new SocEventMessage(
                    EventType: "HintUnlocked",
                    Category: SocCategory.PlatformSecurity,
                    Severity: SocSeverity.Info,
                    Details: $"Team {participation.Team.Name} unlocked hint {hintIndex + 1} for {instance.Challenge.Title}.",
                    Source: nameof(GameController),
                    TeamId: participation.TeamId,
                    UserId: user.Id,
                    GameId: id,
                    ChallengeId: challengeId));
            }
        }

        return Ok(new ChallengeHintDetailModel
        {
            Index = hintIndex,
            Content = hints[hintIndex],
            Penalty = penalty,
            IsUnlocked = true
        });
    }

    /// <summary>
    /// Download the personalized artifact assigned to the caller's accepted team participation.
    /// </summary>
    [RequireUser]
    [HttpGet("{id:int}/Challenges/{challengeId:int}/Artifact")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadPersonalizedArtifact([FromRoute] int id,
        [FromRoute] int challengeId, CancellationToken token)
    {
        var context = await GetContextInfo(id, token: token);
        if (context.Result is not null)
            return context.Result;

        var permission = await divisionRepository.GetPermission(context.Participation?.DivisionId, challengeId, token);
        if (!permission.HasFlag(GamePermission.ViewChallenge))
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Challenge_NotFound)],
                StatusCodes.Status404NotFound));

        var instance = await gameInstanceRepository.GetInstance(context.Participation!, challengeId, token);
        if (instance is null || instance.Challenge.Type != ChallengeType.DynamicAttachment ||
            instance.FlagId is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_ChallengeNotFound)],
                StatusCodes.Status404NotFound));

        var assignment = await db.VariantAssignments
            .Include(a => a.FlagContext)
            .ThenInclude(f => f.Attachment)
            .ThenInclude(a => a!.LocalFile)
            .SingleOrDefaultAsync(a => a.ChallengeId == challengeId &&
                a.ParticipationId == context.Participation!.Id &&
                a.FlagContextId == instance.FlagId, token);

        var attachment = assignment?.FlagContext.Attachment;
        if (assignment is null || attachment is null)
            return NotFound();

        var now = DateTimeOffset.UtcNow;
        assignment.RegisterDownload(now);
        socEvents.Enqueue(new SocEventMessage(
            EventType: "PersonalizedArtifactDownloaded",
            Category: SocCategory.PlatformSecurity,
            Severity: SocSeverity.Info,
            Details: $"Team {context.Participation!.Team.Name} downloaded its personalized artifact.",
            Source: nameof(GameController),
            TeamId: context.Participation.TeamId,
            UserId: context.User!.Id,
            GameId: id,
            ChallengeId: challengeId,
            PayloadJson: System.Text.Json.JsonSerializer.Serialize(new
            {
                assignment.AssignmentId,
                assignment.ArtifactHash,
                assignment.DownloadCount
            })));
        await db.SaveChangesAsync(token);

        Response.Headers.CacheControl = "private, no-store, no-cache, max-age=0";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";
        Response.Headers.Vary = "Cookie, Authorization";

        if (attachment.Type == FileType.Remote && Uri.TryCreate(attachment.RemoteUrl, UriKind.Absolute, out _))
            return Redirect(attachment.RemoteUrl!);

        var file = attachment.LocalFile;
        if (attachment.Type != FileType.Local || file is null)
            return NotFound();

        var path = StoragePath.Combine(PathHelper.Uploads, file.Hash[..2], file.Hash[2..4], file.Hash);
        if (!await storage.ExistsAsync(path, token))
            return NotFound();

        var stream = await storage.OpenReadAsync(path, token);
        return File(stream, MediaTypeNames.Application.Octet,
            instance.Challenge.FileName ?? file.Name, enableRangeProcessing: true);
    }

    /// <summary>
    /// Download an authored shared challenge attachment after validating the caller's accepted participation.
    /// Static and dynamic container challenges can both include a shared source file; only dynamic
    /// attachment challenges use a team-specific artifact instead.
    /// </summary>
    [RequireUser]
    [HttpGet("{id:int}/Challenges/{challengeId:int}/Attachment")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadChallengeAttachment([FromRoute] int id,
        [FromRoute] int challengeId, CancellationToken token)
    {
        var context = await GetContextInfo(id, token: token);
        if (context.Result is not null)
            return context.Result;

        var permission = await divisionRepository.GetPermission(context.Participation?.DivisionId, challengeId, token);
        if (!permission.HasFlag(GamePermission.ViewChallenge))
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Challenge_NotFound)],
                StatusCodes.Status404NotFound));

        var instance = await gameInstanceRepository.GetInstance(context.Participation!, challengeId, token);
        var attachment = instance?.Challenge.Attachment;
        if (instance is null || instance.Challenge.Type == ChallengeType.DynamicAttachment || attachment is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_ChallengeNotFound)],
                StatusCodes.Status404NotFound));

        Response.Headers.CacheControl = "private, no-store, no-cache, max-age=0";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";
        Response.Headers.Vary = "Cookie, Authorization";

        if (attachment.Type == FileType.Remote && Uri.TryCreate(attachment.RemoteUrl, UriKind.Absolute, out _))
            return Redirect(attachment.RemoteUrl!);

        var file = attachment.LocalFile;
        if (attachment.Type != FileType.Local || file is null)
            return NotFound();

        var path = StoragePath.Combine(PathHelper.Uploads, file.Hash[..2], file.Hash[2..4], file.Hash);
        if (!await storage.ExistsAsync(path, token))
            return NotFound();

        socEvents.Enqueue(new SocEventMessage(
            EventType: "ChallengeAttachmentDownloaded",
            Category: SocCategory.PlatformSecurity,
            Severity: SocSeverity.Info,
            Details: $"Team {context.Participation!.Team.Name} downloaded a static challenge attachment.",
            Source: nameof(GameController),
            TeamId: context.Participation.TeamId,
            UserId: context.User!.Id,
            GameId: id,
            ChallengeId: challengeId));

        var stream = await storage.OpenReadAsync(path, token);
        return File(stream, MediaTypeNames.Application.Octet,
            instance.Challenge.FileName ?? file.Name, enableRangeProcessing: true);
    }

    /// <summary>
    /// Submits a flag
    /// </summary>
    /// <remarks>
    /// Submits a flag; requires User permission and active team participation
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="challengeId">Challenge ID</param>
    /// <param name="model">Flag submission</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved game challenge information</response>
    /// <response code="400">Invalid operation</response>
    /// <response code="404">Game not found</response>
    [RequireUser]
    [HttpPost("{id:int}/Challenges/{challengeId:int}")]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Submit))]
    [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Submit([FromRoute] int id, [FromRoute] int challengeId,
        [FromBody] FlagSubmitModel model, CancellationToken token)
    {
        var submitTime = DateTimeOffset.UtcNow;
        var answer = configService.DecryptApiData(model.Flag);
        if (string.IsNullOrWhiteSpace(answer))
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Model_FlagRequired)]));

        if (answer.Length > Limits.MaxFlagLength)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Model_FlagTooLong)]));

        var context = await GetContextInfo(id, token: token);

        if (context.Result is not null)
            return context.Result;

        if (await ZarrarGuard(id, context.Participation!, challengeId, true, token) is { } zarrarResult)
            return zarrarResult;

        const int maxRetries = 3;
        for (var retry = 0; retry < maxRetries; retry++)
        {
            await using var transaction = await gameInstanceRepository.BeginTransactionAsync(token);

            var instance =
                await gameInstanceRepository.GetInstanceForSubmission(context.Participation!, challengeId, token);

            if (instance is null)
                return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_ChallengeNotFound)],
                    StatusCodes.Status404NotFound));

            // Check if submission exceeds challenge deadline (only reject in non-practice mode)
            var hasExceededDeadline = instance.Challenge.DeadlineUtc is { } deadline && submitTime > deadline;
            if (hasExceededDeadline && !context.Game!.PracticeMode)
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Challenge_DeadlinePassed)]));

            var permission =
                await divisionRepository.GetPermission(context.Participation?.DivisionId, challengeId, token);

            if (!permission.HasFlag(GamePermission.ViewChallenge | GamePermission.SubmitFlags))
                return BadRequest(
                    new RequestResponse(localizer[nameof(Resources.Program.Challenge_SubmissionNoPermission)]));

            // Serialise the count-and-insert below; without it, parallel submissions all read the
            // same pre-insert count and every one of them passes the limit check (finding F-011).
            await submissionRepository.AcquireSubmissionLock(context.Participation!.Id, challengeId, token);

            var currentAttempts =
                await submissionRepository.CountSubmissions(context.Participation!.Id, challengeId, token);

            if (instance.Challenge.SubmissionLimit > 0 && currentAttempts >= instance.Challenge.SubmissionLimit)
            {
                return BadRequest(
                    new RequestResponse(localizer[nameof(Resources.Program.Challenge_SubmissionLimitExceeded)]));
            }

            Submission submission = new()
            {
                Game = context.Game!,
                User = context.User!,
                GameChallenge = instance.Challenge,
                Team = context.Participation!.Team,
                Participation = context.Participation!,
                Status = AnswerResult.FlagSubmitted,
                SubmitTimeUtc = submitTime,
                Answer = answer
            };

            try
            {
                submission = await submissionRepository.AddSubmission(submission, token);
                await transaction.CommitAsync(token);

                await channelWriter.WriteAsync(submission, token);
                return Ok(submission.Id);
            }
            catch (DbUpdateConcurrencyException) when (retry < maxRetries - 1)
            {
                await transaction.RollbackAsync(token);
                await Task.Delay((retry + 1) * 100, token);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(token);
                logger.LogErrorMessage(ex, ex.Message);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new RequestResponse(localizer[nameof(Resources.Program.Error_InternalServerError)],
                        StatusCodes.Status500InternalServerError));
            }
        }

        return StatusCode(StatusCodes.Status409Conflict,
            new RequestResponse(localizer[nameof(Resources.Program.Error_InternalServerError)],
                StatusCodes.Status409Conflict));
    }

    /// <summary>
    /// Queries flag status
    /// </summary>
    /// <remarks>
    /// Queries flag status; requires User permission
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="challengeId">Challenge ID</param>
    /// <param name="submitId">Submission ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved submission status</response>
    /// <response code="404">Submission not found</response>
    [RequireUser]
    [HttpGet("{id:int}/Challenges/{challengeId:int}/Status/{submitId:int}")]
    [ProducesResponseType(typeof(AnswerResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Status([FromRoute] int id, [FromRoute] int challengeId, [FromRoute] int submitId,
        CancellationToken token)
    {
        var claimId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (claimId is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_SubmissionNotFound)],
                StatusCodes.Status404NotFound));

        var submission =
            await submissionRepository.GetSubmission(id, challengeId, Guid.Parse(claimId), submitId, token);

        if (submission is null)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Submission_NotFound)],
                StatusCodes.Status404NotFound));

        return Ok(submission.Status switch
        {
            AnswerResult.CheatDetected => AnswerResult.WrongAnswer,
            var x => x
        });
    }

    /// <summary>
    /// Get writeup information
    /// </summary>
    /// <remarks>
    /// Retrieves post-game writeup submission information; requires User permission
    /// </remarks>
    /// <param name="id"></param>
    /// <param name="token"></param>
    /// <response code="200">Successfully submitted writeup</response>
    /// <response code="400">Submission does not meet requirements</response>
    /// <response code="404">Game not found</response>
    [RequireUser]
    [HttpGet("{id:int}/Writeup")]
    [ProducesResponseType(typeof(BasicWriteupInfoModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetWriteup([FromRoute] int id, CancellationToken token)
    {
        var context = await GetContextInfo(id, denyAfterEnded: false, token: token);

        if (context.Result is not null)
            return context.Result;

        return Ok(BasicWriteupInfoModel.FromParticipation(context.Participation!));
    }

    /// <summary>
    /// Submits a writeup
    /// </summary>
    /// <remarks>
    /// Submits a post-game writeup; requires User permission
    /// </remarks>
    /// <param name="id"></param>
    /// <param name="file">File</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully submitted writeup</response>
    /// <response code="400">Submission does not meet requirements</response>
    /// <response code="404">Game not found</response>
    [RequireUser]
    [HttpPost("{id:int}/Writeup")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [RequestFormLimits(MultipartBodyLengthLimit = Limits.MaxWriteupSize)]
    public async Task<IActionResult> SubmitWriteup([FromRoute] int id, IFormFile file, CancellationToken token)
    {
        var current = DateTimeOffset.UtcNow;
        switch (file.Length)
        {
            case 0:
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.File_SizeZero)]));
            case > 20 * 1024 * 1024:
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.File_SizeTooLarge)]));
        }

        if (file.ContentType != "application/pdf" || Path.GetExtension(file.FileName) != ".pdf")
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.File_PdfOnly)]));

        // Content type and extension are both chosen by the uploader, so the file's own leading
        // bytes decide whether it is really a PDF (finding F-014).
        await using (var probe = file.OpenReadStream())
        {
            if (!await FileSignature.IsPdfAsync(probe, token))
                return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.File_PdfOnly)]));
        }

        var context = await GetContextInfo(id, denyAfterEnded: false, token: token);

        if (context.Result is not null)
            return context.Result;

        var game = context.Game!;

        if (!game.WriteupRequired)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_WriteupNotNeeded)]));

        var part = context.Participation!;
        var team = part.Team;

        if (current > game.WriteupDeadline)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_DeadlineExpired)]));

        var wp = context.Participation!.Writeup;

        if (wp is not null)
            await blobService.DeleteBlob(wp, token);

        part.Writeup = await blobService.CreateOrUpdateBlob(file,
            $"Writeup-{game.Id}-{team.Id}-{current:yyyyMMdd-HH.mm.ss}Z.pdf", token);

        await participationRepository.SaveAsync(token);

        logger.Log(StaticLocalizer[nameof(Resources.Program.Game_WriteupSubmitted), team.Name, game.Title],
            context.User!,
            TaskStatus.Success);

        return Ok();
    }

    /// <summary>
    /// Get the caller team's running containers
    /// </summary>
    /// <remarks>
    /// Lists every container the caller's team currently has running in this game, with the
    /// concurrency limit they count against.
    ///
    /// This exists because instances are shared: a GameInstance is keyed by
    /// (ParticipationId, ChallengeId), so one container serves the whole team. Without this
    /// endpoint container state is only visible one challenge at a time, from inside that
    /// challenge's modal — a player cannot see what teammates have already started, nor how
    /// close the team is to Game.ContainerCountLimit.
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved running containers</response>
    /// <response code="404">Game not found or caller has not joined</response>
    [RequireUser]
    [HttpGet("{id:int}/MyInstances")]
    [ProducesResponseType(typeof(TeamInstancesModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MyInstances([FromRoute] int id, CancellationToken token)
    {
        // Reuses the same context resolution as the container endpoints, so membership,
        // participation status and game visibility are enforced identically — this endpoint
        // cannot expose a team's containers to someone who couldn't already act on them.
        var context = await GetContextInfo(id, token: token);

        if (context.Result is not null)
            return context.Result;

        var instances = await gameInstanceRepository.GetRunningInstances(context.Participation!, token);

        var running = instances
            .Select(i => new TeamInstanceModel
            {
                ChallengeId = i.ChallengeId,
                ChallengeTitle = i.Challenge.Title,
                Category = i.Challenge.Category.ToString(),
                Entry = i.Container!.Entry,
                StartedAt = i.Container.StartedAt,
                ExpectStopAt = i.Container.ExpectStopAt
            })
            .OrderBy(i => i.ExpectStopAt)
            .ToArray();

        return Ok(new TeamInstancesModel
        {
            Instances = running,
            Limit = context.Participation!.ContainerCountLimit ?? context.Game!.ContainerCountLimit
        });
    }

    /// <summary>
    /// Create a container
    /// </summary>
    /// <remarks>
    /// Creates a container for the given challenge
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="challengeId">Challenge ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully created container</response>
    /// <response code="404">Challenge not found</response>
    /// <response code="400">Container creation not allowed for challenge</response>
    [RequireUser]
    [HttpPost("{id:int}/Container/{challengeId:int}")]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Container))]
    [ProducesResponseType(typeof(ContainerInfoModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> CreateContainer([FromRoute] int id, [FromRoute] int challengeId,
        CancellationToken token)
    {
        var context = await GetContextInfo(id, token: token);

        if (context.Result is not null)
            return context.Result;

        if (await ZarrarGuard(id, context.Participation!, challengeId, true, token) is { } zarrarResult)
            return zarrarResult;

        var permission = await divisionRepository.GetPermission(context.Participation?.DivisionId, challengeId, token);

        if (!permission.HasFlag(GamePermission.ViewChallenge))
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Challenge_NotFound)],
                StatusCodes.Status404NotFound));

        var instance = await gameInstanceRepository.GetInstance(context.Participation!, challengeId, token);

        if (instance is null || !instance.Challenge.IsEnabled)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Challenge_NotFound)],
                StatusCodes.Status404NotFound));

        if (!instance.Challenge.Type.IsContainer())
            return BadRequest(
                new RequestResponse(localizer[nameof(Resources.Program.Game_ContainerCreationNotAllowed)]));

        if (instance.IsContainerOperationTooFrequent)
            return RequestResponse.Result(localizer[nameof(Resources.Program.Game_OperationTooFrequent)],
                StatusCodes.Status429TooManyRequests);

        if (instance.Container is not null)
        {
            if (instance.Container.Status == ContainerStatus.Running)
                return BadRequest(
                    new RequestResponse(localizer[nameof(Resources.Program.Game_ContainerAlreadyCreated)]));

            await containerRepository.DestroyContainer(instance.Container, token);
        }

        return await gameInstanceRepository.CreateContainer(instance, context.Participation!.Team, context.User!,
                context.Game!, token) switch
        {
            null or (TaskStatus.Failed, null) => BadRequest(
                new RequestResponse(localizer[nameof(Resources.Program.Game_ContainerCreationFailed)])),
            (TaskStatus.Denied, null) => BadRequest(
                new RequestResponse(localizer[nameof(Resources.Program.Game_ContainerNumberLimitExceeded),
                    context.Participation!.ContainerCountLimit ?? context.Game!.ContainerCountLimit])),
            (TaskStatus.Success, var x) => Ok(ContainerInfoModel.FromContainer(x!)),
            _ => throw new UnreachableException()
        };
    }

    /// <summary>
    /// Extends container lifetime
    /// </summary>
    /// <remarks>
    /// Extends container lifetime; requires User permission and can only be extended two hours within ten minutes before expiration
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="challengeId">Challenge ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully retrieved game challenge container information</response>
    /// <response code="404">Challenge not found</response>
    /// <response code="400">Container not created or cannot be extended</response>
    [RequireUser]
    [HttpPost("{id:int}/Container/{challengeId:int}/Extend")]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Container))]
    [ProducesResponseType(typeof(ContainerInfoModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExtendContainerLifetime([FromRoute] int id, [FromRoute] int challengeId,
        CancellationToken token)
    {
        var context = await GetContextInfo(id, token: token);

        if (context.Result is not null)
            return context.Result;

        var instance = await gameInstanceRepository.GetInstance(context.Participation!, challengeId, token);

        if (instance is null || !instance.Challenge.IsEnabled)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Challenge_NotFound)],
                StatusCodes.Status404NotFound));

        if (!instance.Challenge.Type.IsContainer())
            return BadRequest(
                new RequestResponse(localizer[nameof(Resources.Program.Game_ContainerCreationNotAllowed)]));

        if (instance.Container is null)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_ContainerNotCreated)]));

        if (instance.Container.ExpectStopAt - DateTimeOffset.UtcNow >
            TimeSpan.FromMinutes(containerPolicy.Value.RenewalWindow))
            return BadRequest(
                new RequestResponse(localizer[nameof(Resources.Program.Game_ContainerExtensionNotAvailable)]));

        await containerRepository.ExtendLifetime(instance.Container,
            TimeSpan.FromMinutes(containerPolicy.Value.ExtensionDuration), token);

        return Ok(ContainerInfoModel.FromContainer(instance.Container));
    }

    /// <summary>
    /// Deletes a container
    /// </summary>
    /// <remarks>
    /// Deletes a container; requires User permission
    /// </remarks>
    /// <param name="id">Game ID</param>
    /// <param name="challengeId">Challenge ID</param>
    /// <param name="token"></param>
    /// <response code="200">Successfully deleted container</response>
    /// <response code="404">Challenge not found</response>
    /// <response code="400">Container creation not allowed for challenge</response>
    [RequireUser]
    [HttpDelete("{id:int}/Container/{challengeId:int}")]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Container))]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> DeleteContainer([FromRoute] int id, [FromRoute] int challengeId,
        CancellationToken token)
    {
        var context = await GetContextInfo(id, token: token);

        if (context.Result is not null)
            return context.Result;

        var instance = await gameInstanceRepository.GetInstance(context.Participation!, challengeId, token);

        if (instance is null || !instance.Challenge.IsEnabled)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Challenge_NotFound)],
                StatusCodes.Status404NotFound));

        if (!instance.Challenge.Type.IsContainer())
            return BadRequest(
                new RequestResponse(localizer[nameof(Resources.Program.Game_ContainerCreationNotAllowed)]));

        if (instance.Container is null)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_ContainerNotCreated)]));

        if (instance.IsContainerOperationTooFrequent)
            return RequestResponse.Result(localizer[nameof(Resources.Program.Game_OperationTooFrequent)],
                StatusCodes.Status429TooManyRequests);

        var destroyId = instance.Container.LogId;

        if (!await containerRepository.DestroyContainer(instance.Container, token))
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_ContainerDeletionFailed)]));

        instance.LastContainerOperation = DateTimeOffset.UtcNow;

        await gameEventRepository.AddEvent(
            new()
            {
                Type = EventType.ContainerDestroy,
                GameId = context.Game!.Id,
                TeamId = context.Participation!.TeamId,
                UserId = context.User!.Id,
                Values = [instance.Challenge.Id.ToString(), instance.Challenge.Title]
            }, token);

        logger.Log(
            StaticLocalizer[nameof(Resources.Program.Game_ContainerDeleted), context.Participation!.Team.Name,
                instance.Challenge.Title,
                destroyId],
            context.User, TaskStatus.Success);

        return Ok();
    }

    private async Task<ContextInfo> GetContextInfo(int id, bool denyAfterEnded = true,
        CancellationToken token = default)
    {
        ContextInfo res = new()
        {
            User = await userManager.GetUserAsync(User),
            Game = await gameRepository.GetGameById(id, token)
        };

        if (res.Game is null)
            return res.WithResult(NotFound(new RequestResponse(localizer[nameof(Resources.Program.Game_NotFound)],
                StatusCodes.Status404NotFound)));

        var part = await participationRepository.GetParticipation(res.User!.Id, res.Game.Id, token);

        if (part is null)
            return res.WithResult(
                BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_NotParticipated)])));

        res.Participation = part;

        if (part.Status != ParticipationStatus.Accepted)
            return res.WithResult(
                BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_ParticipationNotAccepted)])));

        if (DateTimeOffset.UtcNow < res.Game.StartTimeUtc)
            return res.WithResult(
                BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_NotStarted),
                    ErrorCodes.GameNotStarted])));

        if (denyAfterEnded && !res.Game.PracticeMode && res.Game.EndTimeUtc < DateTimeOffset.UtcNow)
            return res.WithResult(
                BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Game_Ended)], ErrorCodes.GameEnded)));

        return res;
    }

    /// <summary>
    /// ZARRAR showdown rules for a player action on a challenge. Returns null when the action may
    /// proceed (always, outside showdown games).
    /// </summary>
    private async Task<IActionResult?> ZarrarGuard(int gameId, Participation participation, int challengeId,
        bool blockWhenPaused, CancellationToken token)
    {
        if (!ZarrarDuelService.IsDuelGame(gameId))
            return null;

        var state = await zarrar.GetAsync(gameId, token);
        if (!state.Enabled)
            return null;

        if (state.Ended)
            return BadRequest(new RequestResponse("The match has ended."));

        if (blockWhenPaused && state.Paused)
            return BadRequest(new RequestResponse($"Match paused — {state.PauseReason ?? "wait for the admin"}."));

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var zarrarUser))
            return Unauthorized(new RequestResponse("Sign in to play this match.", StatusCodes.Status401Unauthorized));

        // Tag-team: only the member on the keyboard plays for the team.
        if (await zarrar.BenchedByAsync(gameId, participation.Id, zarrarUser, token) is { } onKeyboard)
        {
            ZarrarSignal("ZarrarBenchedPlayerAction", SocSeverity.Medium,
                "A benched tag-team player tried to act on a challenge while a teammate held the keyboard.",
                gameId, participation, zarrarUser, challengeId);
            return BadRequest(new RequestResponse(onKeyboard.Length > 0
                ? $"You are on the bench — {onKeyboard} is playing for your team. Wait for the monitor to tag you in."
                : "You are on the bench. Wait for the monitor to tag you in."));
        }

        // The player acting must be the one whose screen is on the stage, not just someone on
        // the team (closes the gap while a tag-out's old stream is still closing).
        if (!zarrar.IsStreaming(gameId, participation.Id, zarrarUser))
        {
            // Normal while a player is connecting; only a flag submission off-stream is notable.
            if (blockWhenPaused)
                ZarrarSignal("ZarrarOffStreamAction", SocSeverity.Low,
                    "A showdown player tried to submit or start a container without their own screen on the stage.",
                    gameId, participation, zarrarUser, challengeId);
            return BadRequest(new RequestResponse("Share your entire screen to play this match."));
        }

        if (await ZarrarDuelService.IsCapturedAsync(db, challengeId, token))
            return NotFound(new RequestResponse("This challenge has already been captured.",
                StatusCodes.Status404NotFound));

        return null;
    }

    /// <summary>Showdown anti-cheat signal to the SOC (throttled per player and kind).</summary>
    private void ZarrarSignal(string eventType, SocSeverity severity, string details, int gameId,
        Participation participation, Guid userId, int challengeId) =>
        HttpContext.RequestServices.GetService<BURAQ.Services.Soc.SecuritySignalSink>()?.Signal(
            $"{eventType}:{gameId}:{userId:N}",
            new BURAQ.Services.Soc.SocEventMessage(
                EventType: eventType,
                Category: SocCategory.AntiCheat,
                Severity: severity,
                Details: details,
                Source: "ZarrarGuard",
                TeamId: participation.TeamId,
                UserId: userId,
                ActorUserId: userId,
                GameId: gameId,
                ChallengeId: challengeId,
                PayloadJson: System.Text.Json.JsonSerializer.Serialize(new { participationId = participation.Id })));

    private static string GameETag(int gameId, DateTimeOffset lastModified) =>
        $"\"{gameId}-{lastModified.ToUnixTimeSeconds():X}\"";

    private static Dictionary<ChallengeCategory, IEnumerable<ChallengeInfo>> FilterChallengesByPermission(
        Dictionary<ChallengeCategory, IEnumerable<ChallengeInfo>> challenges,
        DivisionItem division)
    {
        var res = new Dictionary<ChallengeCategory, IEnumerable<ChallengeInfo>>();

        foreach ((var cat, var chs) in challenges)
        {
            var infos = chs.Where(chal =>
                division.ChallengeConfigs.TryGetValue(chal.Id, out var config)
                    ? config.Permissions.HasFlag(GamePermission.ViewChallenge)
                    : division.DefaultPermissions.HasFlag(GamePermission.ViewChallenge)
            ).ToArray();

            if (infos.Length > 0)
                res[cat] = infos;
        }

        return res;
    }

    private class ContextInfo
    {
        public Game? Game;
        public Participation? Participation;
        public UserInfo? User;

        /// <summary>
        /// The result to be returned.
        /// If this is not null, the action should return this result directly.
        /// </summary>
        public IActionResult? Result;

        public ContextInfo WithResult(IActionResult res)
        {
            Result = res;
            return this;
        }
    }
}
