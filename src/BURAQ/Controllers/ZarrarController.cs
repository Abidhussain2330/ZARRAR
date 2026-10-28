using System.Net.Mime;
using System.Text.RegularExpressions;
using System.Security.Claims;
using BURAQ.Middlewares;
using BURAQ.Models.Request.Zarrar;
using BURAQ.Repositories.Interface;
using BURAQ.Services.Soc;
using BURAQ.Services.Zarrar;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace BURAQ.Controllers;

/// <summary>
/// ZARRAR 1v1 showdown: match control, stage snapshot and the live screen relay.
/// </summary>
[ApiController]
[Route("api/zarrar")]
[Produces(MediaTypeNames.Application.Json)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
public partial class ZarrarController(
    ZarrarDuelService duel,
    LiveRelay relay,
    IGameRepository gameRepository,
    IParticipationRepository participationRepository,
    AppDbContext db) : ControllerBase
{
    // ------------------------------------------------------------------ monitor

    /// <summary>
    /// Games for the monitor's event picker (showdowns first).
    /// </summary>
    [RequireMonitor]
    [HttpGet("games")]
    [ProducesResponseType(typeof(DuelGameModel[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> Games(CancellationToken token) =>
        Ok(await duel.ListGamesAsync(await ContextHelper.HasAdmin(HttpContext), token));

    /// <summary>
    /// Full stage snapshot: players, scores, challenges, live feeds and match state.
    /// </summary>
    [RequireMonitor]
    [HttpGet("{id:int}/state")]
    [ProducesResponseType(typeof(DuelSnapshotModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> State([FromRoute] int id, CancellationToken token)
    {
        if (!await gameRepository.HasGameAsync(id, token))
            return NotFound(new RequestResponse("Game not found", StatusCodes.Status404NotFound));

        return Ok(await duel.BuildSnapshotAsync(id, token, await ContextHelper.HasAdmin(HttpContext)));
    }

    /// <summary>
    /// Watch one player's screen (WebSocket).
    /// </summary>
    [RequireMonitor]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Query))]
    [Route("{id:int}/live/watch/{participationId:int}")]
    public async Task<IActionResult> Watch([FromRoute] int id, [FromRoute] int participationId,
        CancellationToken token)
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
            return BadRequest(new RequestResponse("WebSocket required"));

        var exists = await db.Participations.AsNoTracking()
            .AnyAsync(p => p.Id == participationId && p.GameId == id, token);

        if (!exists)
            return NotFound(new RequestResponse("Player not found", StatusCodes.Status404NotFound));

        using var socket = await HttpContext.WebSockets.AcceptWebSocketAsync();
        await relay.WatchAsync(socket, id, participationId, token);
        return new EmptyResult();
    }

    /// <summary>
    /// Tag-in: put a team member on the keyboard. Their teammate's stream is closed, and only
    /// the tagged-in player may stream and submit flags for the team. A null user clears it.
    /// </summary>
    [RequireMonitor]
    [HttpPut("{id:int}/operator")]
    [ProducesResponseType(typeof(DuelStateModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Operator([FromRoute] int id, [FromBody] DuelOperatorModel model,
        CancellationToken token)
    {
        if (!await gameRepository.HasGameAsync(id, token))
            return NotFound(new RequestResponse("Game not found", StatusCodes.Status404NotFound));

        var (state, error) = await duel.SetOperatorAsync(id, model.ParticipationId, model.UserId, token);
        if (state is null)
            return BadRequest(new RequestResponse(error ?? "Could not tag that player in"));

        // Who changed the player on the keyboard, and to whom: an audit trail for disputes.
        await HttpContext.RequestServices.GetRequiredService<ISocEventPublisher>().PublishAsync(new SocEventMessage(
            EventType: "ZarrarOperatorChanged",
            Category: SocCategory.AntiCheat,
            Severity: SocSeverity.Info,
            Details: model.UserId is null
                ? "The tag-team choice for a team was cleared."
                : "A tag-team player was put on the keyboard.",
            Source: nameof(ZarrarController),
            UserId: model.UserId,
            GameId: id,
            PayloadJson: System.Text.Json.JsonSerializer.Serialize(new { model.ParticipationId, model.UserId })), token);

        return Ok(state);
    }

    // -------------------------------------------------------------------- admin

    /// <summary>
    /// Turn showdown mode on or off for a game.
    /// </summary>
    [RequireAdmin]
    [HttpPut("{id:int}/mode")]
    [ProducesResponseType(typeof(DuelStateModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Mode([FromRoute] int id, [FromBody] DuelEnableModel model,
        CancellationToken token)
    {
        if (!await gameRepository.HasGameAsync(id, token))
            return NotFound(new RequestResponse("Game not found", StatusCodes.Status404NotFound));

        return Ok(await duel.SetEnabledAsync(id, model.Enabled, token));
    }

    /// <summary>
    /// Change what the stage shows: split, p1, p2 or board.
    /// </summary>
    [RequireAdmin]
    [HttpPut("{id:int}/layout")]
    [ProducesResponseType(typeof(DuelStateModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Layout([FromRoute] int id, [FromBody] DuelLayoutModel model,
        CancellationToken token) =>
        await GameMissing(id, token) ?? Ok(await duel.SetLayoutAsync(id, model.Layout, token));

    /// <summary>
    /// Start the match: plays the 3-2-1 countdown on the stage. The admin page releases the first
    /// challenge when the countdown ends.
    /// </summary>
    [RequireAdmin]
    [HttpPost("{id:int}/start")]
    [ProducesResponseType(typeof(DuelStateModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Start([FromRoute] int id, CancellationToken token)
    {
        if (!await gameRepository.HasGameAsync(id, token))
            return NotFound(new RequestResponse("Game not found", StatusCodes.Status404NotFound));

        return Ok(await duel.StartMatchAsync(id, token));
    }

    /// <summary>
    /// Pause the match (blocks flag submission for both players).
    /// </summary>
    [RequireAdmin]
    [HttpPost("{id:int}/pause")]
    [ProducesResponseType(typeof(DuelStateModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Pause([FromRoute] int id, CancellationToken token) =>
        await GameMissing(id, token) ?? Ok(await duel.PauseAsync(id, "PAUSED BY ADMIN", token));

    /// <summary>
    /// Resume the match; the game end time moves back by the paused duration.
    /// </summary>
    [RequireAdmin]
    [HttpPost("{id:int}/resume")]
    [ProducesResponseType(typeof(DuelStateModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Resume([FromRoute] int id, CancellationToken token) =>
        await GameMissing(id, token) ?? Ok(await duel.ResumeAsync(id, token));

    /// <summary>
    /// End the match now.
    /// </summary>
    [RequireAdmin]
    [HttpPost("{id:int}/end")]
    [ProducesResponseType(typeof(DuelStateModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> End([FromRoute] int id, CancellationToken token) =>
        await GameMissing(id, token) ?? Ok(await duel.EndAsync(id, token));

    /// <summary>
    /// Undo "end match" (the game end time is not restored; edit it in game settings).
    /// </summary>
    [RequireAdmin]
    [HttpPost("{id:int}/reopen")]
    [ProducesResponseType(typeof(DuelStateModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Reopen([FromRoute] int id, CancellationToken token) =>
        await GameMissing(id, token) ?? Ok(await duel.ReopenAsync(id, token));

    /// <summary>
    /// Announce a challenge release on the stage. The challenge itself is enabled through the
    /// normal challenge edit API first, which creates instances and notifies players.
    /// </summary>
    [RequireAdmin]
    [HttpPost("{id:int}/announce")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Announce([FromRoute] int id, [FromBody] DuelReleaseModel model,
        CancellationToken token)
    {
        if (await GameMissing(id, token) is { } missing)
            return missing;

        await duel.AnnounceReleaseAsync(id, model.ChallengeId, token);
        return Ok();
    }

    /// <summary>
    /// Mark a challenge withdrawn (no winner). Disable it through the challenge edit API first.
    /// </summary>
    [RequireAdmin]
    [HttpPost("{id:int}/withdraw")]
    [ProducesResponseType(typeof(DuelStateModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Withdraw([FromRoute] int id, [FromBody] DuelReleaseModel model,
        CancellationToken token)
    {
        if (await GameMissing(id, token) is { } missing)
            return missing;

        // Only a challenge of this game can be marked withdrawn.
        if (!await db.GameChallenges.AsNoTracking().AnyAsync(c => c.Id == model.ChallengeId && c.GameId == id, token))
            return NotFound(new RequestResponse("Challenge not found", StatusCodes.Status404NotFound));

        return Ok(await duel.MarkWithdrawnAsync(id, model.ChallengeId, token));
    }

    /// <summary>
    /// The match timeline: releases, wrong flags, captures, pauses, lost feeds (oldest first).
    /// </summary>
    [RequireAdmin]
    [HttpGet("{id:int}/timeline")]
    [ProducesResponseType(typeof(List<StageEventModel>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Timeline([FromRoute] int id, CancellationToken token) =>
        await GameMissing(id, token) ?? Ok(await duel.GetTimelineAsync(id, token));

    /// <summary>
    /// Recorded screen feeds of a match.
    /// </summary>
    [RequireAdmin]
    [HttpGet("{id:int}/recordings")]
    [ProducesResponseType(typeof(DuelRecordingModel[]), StatusCodes.Status200OK)]
    public IActionResult Recordings([FromRoute] int id)
    {
        var dir = relay.RecordingDirectory(id);
        if (!Directory.Exists(dir))
            return Ok(Array.Empty<DuelRecordingModel>());

        var files = new DirectoryInfo(dir).GetFiles("*.ivf")
            .OrderBy(f => f.Name, StringComparer.Ordinal)
            .Select(f =>
            {
                var match = RecordingParticipation().Match(f.Name);
                return new DuelRecordingModel
                {
                    Name = f.Name,
                    Bytes = f.Length,
                    ModifiedUtc = f.LastWriteTimeUtc,
                    ParticipationId = match.Success && int.TryParse(match.Groups[1].Value, out var pid) ? pid : null
                };
            })
            .ToArray();

        return Ok(files);
    }

    /// <summary>
    /// Download one recording (IVF; plays in VLC, or <c>ffmpeg -i in.ivf -c copy out.webm</c>).
    /// </summary>
    [RequireAdmin]
    [HttpGet("{id:int}/recordings/{name}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    public IActionResult Recording([FromRoute] int id, [FromRoute] string name)
    {
        // Only a bare file name from the listing: no separators, no traversal.
        if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) || name.Contains("..")
            || !name.EndsWith(".ivf", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new RequestResponse("Invalid recording name"));

        var dir = relay.RecordingDirectory(id);
        var path = Path.GetFullPath(Path.Combine(dir, name));

        if (!path.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || !System.IO.File.Exists(path))
            return NotFound(new RequestResponse("Recording not found", StatusCodes.Status404NotFound));

        return PhysicalFile(path, "video/x-ivf", name, enableRangeProcessing: true);
    }

    [GeneratedRegex(@"-p(\d+)-")]
    private static partial Regex RecordingParticipation();

    // ------------------------------------------------------------------- player

    /// <summary>
    /// The caller's view of the showdown: whether it is one, paused, ended, and whether the
    /// server is receiving their screen.
    /// </summary>
    [RequireUser]
    [HttpGet("{id:int}/me")]
    [ProducesResponseType(typeof(DuelPlayerStatusModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Me([FromRoute] int id, CancellationToken token)
    {
        if (!ZarrarDuelService.IsDuelGame(id))
            return Ok(new DuelPlayerStatusModel { Duel = false });

        var part = await GetAcceptedParticipation(id, token);
        var state = await duel.GetAsync(id, token);

        var hasUser = TryGetUserId(out var userId);
        string? benchedBy = null;
        string? teamName = null;
        if (part is not null && hasUser)
        {
            benchedBy = await duel.BenchedByAsync(id, part.Id, userId, token);
            teamName = await db.Teams.AsNoTracking()
                .Where(t => t.Id == part.TeamId)
                .Select(t => t.Name)
                .FirstOrDefaultAsync(token);
        }

        // "Live" means the caller's own screen is on the stage, not a teammate's.
        var live = part is not null && hasUser && relay.PublisherOf(id, part.Id) is { } pub && pub.UserId == userId;

        return Ok(new DuelPlayerStatusModel
        {
            Duel = state.Enabled,
            Paused = state.Paused,
            PauseReason = state.PauseReason,
            Ended = state.Ended,
            Live = live,
            Benched = benchedBy is not null,
            OperatorName = benchedBy is { Length: > 0 } ? benchedBy : null,
            TeamName = teamName
        });
    }

    /// <summary>
    /// Publish the caller's screen (WebSocket). Only accepted players of a showdown game.
    /// </summary>
    [RequireUser]
    [EnableRateLimiting(nameof(RateLimiter.LimitPolicy.Query))]
    [Route("{id:int}/live/publish")]
    public async Task<IActionResult> Publish([FromRoute] int id, CancellationToken token)
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
            return BadRequest(new RequestResponse("WebSocket required"));

        if (!ZarrarDuelService.IsDuelGame(id))
            return BadRequest(new RequestResponse("This game is not a ZARRAR showdown"));

        var part = await GetAcceptedParticipation(id, token);
        if (part is null)
            return BadRequest(new RequestResponse("You are not an accepted player in this match"));

        if (!TryGetUserId(out var userId))
            return Unauthorized();

        var teamName = await db.Teams.AsNoTracking()
            .Where(t => t.Id == part.TeamId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(token) ?? $"Player {part.Id}";

        var userName = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.UserName)
            .FirstOrDefaultAsync(token) ?? "player";

        using var socket = await HttpContext.WebSockets.AcceptWebSocketAsync();

        // Tag-team: a benched teammate is told so over the socket (close code 4001), which stops
        // their browser from retrying; a plain HTTP refusal would just loop reconnects.
        if (await duel.BenchedByAsync(id, part.Id, userId, token) is not null)
        {
            // Trying to stream over the teammate on the keyboard: a stream hijack or a confused
            // player. The SOC sees it either way.
            HttpContext.RequestServices.GetService<SecuritySignalSink>()?.Signal(
                $"ZarrarBenchedPublish:{id}:{userId:N}",
                new SocEventMessage(
                    EventType: "ZarrarBenchedPublish",
                    Category: SocCategory.AntiCheat,
                    Severity: SocSeverity.Medium,
                    Details: "A benched tag-team player tried to stream their screen over the teammate on the keyboard.",
                    Source: nameof(ZarrarController),
                    TeamId: part.TeamId,
                    UserId: userId,
                    ActorUserId: userId,
                    GameId: id,
                    PayloadJson: System.Text.Json.JsonSerializer.Serialize(new { participationId = part.Id })));

            await LiveRelay.RejectBenchedAsync(socket);
            return new EmptyResult();
        }

        await relay.PublishAsync(socket, id, part.Id, teamName, userId, userName, token);
        return new EmptyResult();
    }

    private async Task<IActionResult?> GameMissing(int id, CancellationToken token) =>
        await gameRepository.HasGameAsync(id, token)
            ? null
            : NotFound(new RequestResponse("Game not found", StatusCodes.Status404NotFound));

    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private async Task<Participation?> GetAcceptedParticipation(int gameId, CancellationToken token)
    {
        if (!TryGetUserId(out var userId))
            return null;

        var part = await participationRepository.GetParticipation(userId, gameId, token);
        return part is { Status: ParticipationStatus.Accepted } ? part : null;
    }
}
