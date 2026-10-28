using System.Collections.Concurrent;
using System.Text.Json;
using BURAQ.Hubs;
using BURAQ.Hubs.Clients;
using BURAQ.Models.Request.Game;
using BURAQ.Models.Request.Zarrar;
using BURAQ.Repositories.Interface;
using BURAQ.Services.Cache;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BURAQ.Services.Zarrar;

/// <summary>
/// The ZARRAR 1v1 showdown: one BURAQ game is one match between two solo players.
/// </summary>
/// <remarks>
/// <para>
/// State lives in memory and is persisted as JSON in the existing <c>Configs</c> table, so the
/// feature ships without a schema migration. Rules layered on top of the normal game:
/// </para>
/// <list type="bullet">
/// <item>The first correct flag captures a challenge. It disappears for both players, its
/// containers are destroyed, and only that first solve scores.</item>
/// <item>A player must be streaming their screen to open a challenge, start a container or
/// submit a flag.</item>
/// <item>If a feed drops for longer than a short grace period, the match pauses. Only the admin
/// resumes it, and the game end time moves back by the paused duration.</item>
/// </list>
/// Single-instance by design (the venue runs one server).
/// </remarks>
public sealed class ZarrarDuelService : IHostedService
{
    /// <summary>Advisory-lock namespace used to serialise captures ("ZARR").</summary>
    internal const int CaptureLockNamespace = 0x5A415252;

    private const string ConfigKeyPrefix = "ZARRAR_DUEL_";
    private const string LogKeyPrefix = "ZARRAR_LOG_";
    private const int MaxLogEntries = 5000;
    private static readonly TimeSpan FeedLossGrace = TimeSpan.FromSeconds(4);
    private static readonly HashSet<string> Layouts = ["split", "p1", "p2", "board", "f1", "f2"];

    /// <summary>
    /// Snapshot of which games are showdowns, readable from code that cannot take a dependency
    /// on this service (repositories constructed per scope).
    /// </summary>
    private static readonly ConcurrentDictionary<int, bool> EnabledGames = new();

    private readonly ConcurrentDictionary<int, DuelStateModel> _states = new();
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _locks = new();
    private readonly ConcurrentDictionary<int, List<StageEventModel>> _logs = new();
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _logLocks = new();

    /// <summary>Games whose timeline has unsaved events, with a flush already scheduled.</summary>
    private readonly ConcurrentDictionary<int, byte> _logFlushPending = new();

    /// <summary>
    /// Timeline writes are batched: a burst of wrong flags becomes one database write instead of
    /// one full rewrite of the timeline per flag.
    /// </summary>
    private static readonly TimeSpan LogFlushDelay = TimeSpan.FromSeconds(2);

    /// <summary>Stage events kept in the match timeline (the "accepted" echo of a capture is skipped).</summary>
    private static readonly HashSet<string> LoggedEvents =
    [
        "countdown", "released", "denied", "captured", "withdrawn", "feedLost", "feedRestored", "paused",
        "resumed", "ended", "tagged"
    ];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<ZarrarHub, IZarrarClient> _hub;
    private readonly LiveRelay _relay;
    private readonly ILogger<ZarrarDuelService> _logger;
    private readonly BURAQ.Services.Soc.SecuritySignalSink _signals;

    public ZarrarDuelService(IServiceScopeFactory scopeFactory, IHubContext<ZarrarHub, IZarrarClient> hub,
        LiveRelay relay, BURAQ.Services.Soc.SecuritySignalSink signals, ILogger<ZarrarDuelService> logger)
    {
        _scopeFactory = scopeFactory;
        _hub = hub;
        _relay = relay;
        _signals = signals;
        _logger = logger;
        _relay.FeedChanged += OnFeedChanged;
    }

    /// <summary>
    /// Whether the game runs as a showdown. Cheap and static so hot paths can call it freely.
    /// </summary>
    public static bool IsDuelGame(int gameId) => EnabledGames.TryGetValue(gameId, out var on) && on;

    public bool IsLive(int gameId, int participationId) => _relay.IsLive(gameId, participationId);

    /// <summary>Whether this user's own screen is the team's live feed.</summary>
    public bool IsStreaming(int gameId, int participationId, Guid userId) =>
        _relay.PublisherOf(gameId, participationId) is { } live && live.UserId == userId;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var rows = await db.Configs.AsNoTracking()
                .Where(c => c.ConfigKey.StartsWith(ConfigKeyPrefix))
                .ToListAsync(cancellationToken);

            foreach (var row in rows)
            {
                var state = Deserialize(row.Value);
                if (state is null)
                    continue;

                _states[state.GameId] = state;
                EnabledGames[state.GameId] = state.Enabled;
            }

            _logger.SystemLog($"ZARRAR showdown state loaded for {rows.Count} game(s)", TaskStatus.Success,
                LogLevel.Debug);
        }
        catch (Exception e)
        {
            // A fresh database may not be migrated yet when hosted services start.
            _logger.LogErrorMessage(e, "ZARRAR could not load showdown state");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Don't lose the last couple of seconds of timeline on a clean shutdown.
        foreach (var gameId in _logFlushPending.Keys.ToArray())
        {
            _logFlushPending.TryRemove(gameId, out _);
            await FlushLogAsync(gameId);
        }
    }

    /// <summary>
    /// Current state (a copy; mutate through <see cref="UpdateAsync" />).
    /// </summary>
    public async Task<DuelStateModel> GetAsync(int gameId, CancellationToken token = default)
    {
        if (_states.TryGetValue(gameId, out var cached))
            return cached.Clone();

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var key = ConfigKeyPrefix + gameId;
        var row = await db.Configs.AsNoTracking().FirstOrDefaultAsync(c => c.ConfigKey == key, token);

        var state = Deserialize(row?.Value) ?? new DuelStateModel { GameId = gameId };
        state.GameId = gameId;
        _states[gameId] = state;
        EnabledGames[gameId] = state.Enabled;
        return state.Clone();
    }

    /// <summary>
    /// Applies a change, persists it and pushes it to the stage and both players.
    /// </summary>
    /// <param name="gameId">The game the match belongs to.</param>
    /// <param name="mutate">Returns false to abandon the change.</param>
    /// <param name="token">Cancellation token.</param>
    public async Task<DuelStateModel> UpdateAsync(int gameId, Func<DuelStateModel, bool> mutate,
        CancellationToken token = default)
    {
        var gate = _locks.GetOrAdd(gameId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token);

        try
        {
            var state = await GetAsync(gameId, token);
            if (!mutate(state))
                return state;

            state.GameId = gameId;
            state.UpdatedUtc = DateTimeOffset.UtcNow;

            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var key = ConfigKeyPrefix + gameId;
                var json = JsonSerializer.Serialize(state, AppJsonSerializerContext.Default.DuelStateModel);
                var row = await db.Configs.FirstOrDefaultAsync(c => c.ConfigKey == key, token);

                if (row is null)
                    db.Configs.Add(new global::BURAQ.Models.Data.Config(key, json));
                else
                    row.Value = json;

                await db.SaveChangesAsync(token);
            }

            _states[gameId] = state;
            EnabledGames[gameId] = state.Enabled;

            var copy = state.Clone();

            // Players only need pause/resume/end; they get no user IDs of either team.
            var forPlayers = state.Clone();
            forPlayers.Operators = new Dictionary<int, Guid>();

            // The change is saved; a hub hiccup (e.g. the Redis backplane reconnecting) must not
            // turn it into an error for the caller. Clients also poll, so they catch up.
            try
            {
                await _hub.Clients.Group(ZarrarHub.StageGroup(gameId)).DuelState(copy);
                await _hub.Clients.Group(ZarrarHub.PlayerGroup(gameId)).DuelState(forPlayers);
            }
            catch (Exception e)
            {
                _logger.LogErrorMessage(e, "ZARRAR could not push the match state");
            }

            return copy;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Sends a moment to the stage (monitors and admins only).
    /// </summary>
    public async Task BroadcastAsync(StageEventModel stageEvent)
    {
        stageEvent.TimeUtc = DateTimeOffset.UtcNow;

        if (LoggedEvents.Contains(stageEvent.Type))
            await AppendLogAsync(stageEvent);

        try
        {
            await _hub.Clients.Group(ZarrarHub.StageGroup(stageEvent.GameId)).StageEvent(stageEvent);
        }
        catch (Exception e)
        {
            // The timeline already has it; the stage refreshes from its snapshot poll.
            _logger.LogErrorMessage(e, "ZARRAR could not push a stage event");
        }
    }

    /// <summary>
    /// The match timeline: every release, wrong flag, capture, pause and lost feed, oldest first.
    /// </summary>
    public async Task<List<StageEventModel>> GetTimelineAsync(int gameId, CancellationToken token = default)
    {
        var gate = _logLocks.GetOrAdd(gameId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token);

        try
        {
            return [.. await LoadLogAsync(gameId, token)];
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task AppendLogAsync(StageEventModel stageEvent)
    {
        var gameId = stageEvent.GameId;
        var gate = _logLocks.GetOrAdd(gameId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();

        try
        {
            var log = await LoadLogAsync(gameId, CancellationToken.None);
            log.Add(stageEvent);
            if (log.Count > MaxLogEntries)
                log.RemoveRange(0, log.Count - MaxLogEntries);
        }
        catch (Exception e)
        {
            _logger.LogErrorMessage(e, "ZARRAR could not record a timeline event");
            return;
        }
        finally
        {
            gate.Release();
        }

        if (_logFlushPending.TryAdd(gameId, 0))
            _ = Task.Run(async () =>
            {
                await Task.Delay(LogFlushDelay);
                // Cleared before writing, so events arriving during the write schedule another flush.
                _logFlushPending.TryRemove(gameId, out _);
                await FlushLogAsync(gameId);
            });
    }

    /// <summary>Writes a game's cached timeline to the database.</summary>
    private async Task FlushLogAsync(int gameId)
    {
        var gate = _logLocks.GetOrAdd(gameId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();

        try
        {
            if (!_logs.TryGetValue(gameId, out var log))
                return;

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var key = LogKeyPrefix + gameId;
            var json = JsonSerializer.Serialize(log, AppJsonSerializerContext.Default.ListStageEventModel);
            var row = await db.Configs.FirstOrDefaultAsync(c => c.ConfigKey == key);

            if (row is null)
                db.Configs.Add(new global::BURAQ.Models.Data.Config(key, json));
            else
                row.Value = json;

            await db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            // The timeline is a record, not a rule: never let it stop the stage.
            _logger.LogErrorMessage(e, "ZARRAR could not write the match timeline");
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Loads (and caches) a game's timeline. Call with the log lock held.</summary>
    private async Task<List<StageEventModel>> LoadLogAsync(int gameId, CancellationToken token)
    {
        if (_logs.TryGetValue(gameId, out var cached))
            return cached;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var key = LogKeyPrefix + gameId;
        var value = await db.Configs.AsNoTracking()
            .Where(c => c.ConfigKey == key)
            .Select(c => c.Value)
            .FirstOrDefaultAsync(token);

        List<StageEventModel> log = [];
        if (!string.IsNullOrWhiteSpace(value))
        {
            try
            {
                log = JsonSerializer.Deserialize(value, AppJsonSerializerContext.Default.ListStageEventModel) ?? [];
            }
            catch (JsonException)
            {
                log = [];
            }
        }

        _logs[gameId] = log;
        return log;
    }

    public Task<DuelStateModel> SetEnabledAsync(int gameId, bool enabled, CancellationToken token = default) =>
        UpdateAsync(gameId, s =>
        {
            s.Enabled = enabled;
            if (!enabled)
            {
                s.Paused = false;
                s.PauseReason = null;
                s.PausedAtUtc = null;
            }

            return true;
        }, token);

    public Task<DuelStateModel> SetLayoutAsync(int gameId, string layout, CancellationToken token = default) =>
        UpdateAsync(gameId, s =>
        {
            if (!Layouts.Contains(layout))
                return false;

            s.Layout = layout;
            return true;
        }, token);

    public async Task<DuelStateModel> PauseAsync(int gameId, string reason, CancellationToken token = default)
    {
        var paused = false;
        var state = await UpdateAsync(gameId, s =>
        {
            if (!s.Enabled || s.Paused || s.Ended)
                return false;

            var now = DateTimeOffset.UtcNow;
            s.StopClock(now);
            s.Paused = true;
            s.PauseReason = reason;
            s.PausedAtUtc = now;
            paused = true;
            return true;
        }, token);

        if (paused)
            await BroadcastAsync(new StageEventModel { Type = "paused", GameId = gameId, Message = reason });

        return state;
    }

    /// <summary>
    /// Resumes a paused match and pushes the game end time back by the paused duration.
    /// </summary>
    public async Task<DuelStateModel> ResumeAsync(int gameId, CancellationToken token = default)
    {
        TimeSpan pausedFor = TimeSpan.Zero;
        DateTimeOffset? pausedAt = null;
        var resumed = false;

        var state = await UpdateAsync(gameId, s =>
        {
            if (!s.Paused)
                return false;

            var now = DateTimeOffset.UtcNow;
            pausedAt = s.PausedAtUtc;
            pausedFor = s.PausedAtUtc is { } at ? now - at : TimeSpan.Zero;
            s.Paused = false;
            s.PauseReason = null;
            s.PausedAtUtc = null;
            if (s.ReleasedAtClock.Count > 0)
                s.RunClock(now);
            resumed = true;
            return true;
        }, token);

        if (!resumed)
            return state;

        if (pausedFor > TimeSpan.FromSeconds(1))
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var games = scope.ServiceProvider.GetRequiredService<IGameRepository>();
            var game = await games.GetGameById(gameId, token);

            // Compare with when the pause began, not now: a pause that runs past the scheduled end
            // must still give the players their remaining time back.
            if (game is not null && pausedAt is { } began && game.EndTimeUtc > began)
            {
                game.EndTimeUtc += pausedFor;
                await games.UpdateGame(game, token);
            }
        }

        await BroadcastAsync(new StageEventModel
        {
            Type = "resumed",
            GameId = gameId,
            Message = pausedFor > TimeSpan.Zero ? $"+{(int)pausedFor.TotalSeconds}s added to the clock" : null
        });

        return state;
    }

    /// <summary>
    /// Ends the match now: the game end time is set to now, so the scoreboard freezes.
    /// </summary>
    public async Task<DuelStateModel> EndAsync(int gameId, CancellationToken token = default)
    {
        var state = await UpdateAsync(gameId, s =>
        {
            s.StopClock(DateTimeOffset.UtcNow);
            s.Ended = true;
            s.Paused = false;
            s.PauseReason = null;
            s.PausedAtUtc = null;
            s.Layout = "board";
            return true;
        }, token);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var games = scope.ServiceProvider.GetRequiredService<IGameRepository>();
        var game = await games.GetGameById(gameId, token);

        if (game is not null && game.EndTimeUtc > DateTimeOffset.UtcNow)
        {
            game.EndTimeUtc = DateTimeOffset.UtcNow;
            await games.UpdateGame(game, token);
        }

        var snapshot = game is null ? null : await BuildSnapshotAsync(gameId, token);
        var winner = snapshot?.Players.OrderByDescending(p => p.Score).ThenBy(p => p.Rank).FirstOrDefault();

        await BroadcastAsync(new StageEventModel
        {
            Type = "ended",
            GameId = gameId,
            ParticipationId = winner?.ParticipationId,
            TeamName = winner?.TeamName,
            Points = winner?.Score
        });

        return state;
    }

    /// <summary>
    /// Reopens a match that was ended by mistake (does not restore the end time).
    /// </summary>
    public Task<DuelStateModel> ReopenAsync(int gameId, CancellationToken token = default) =>
        UpdateAsync(gameId, s =>
        {
            if (!s.Ended)
                return false;

            s.Ended = false;
            s.Layout = "split";
            if (s.ReleasedAtClock.Count > 0)
                s.RunClock(DateTimeOffset.UtcNow);
            return true;
        }, token);

    /// <summary>
    /// Announces a released challenge on the stage.
    /// </summary>
    public async Task AnnounceReleaseAsync(int gameId, int challengeId, CancellationToken token = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var challenge = await db.GameChallenges.AsNoTracking()
            .Where(c => c.Id == challengeId && c.GameId == gameId)
            .Select(c => new { c.Id, c.Title, c.OriginalScore, c.IsEnabled })
            .FirstOrDefaultAsync(token);

        if (challenge is null || !challenge.IsEnabled)
            return;

        await UpdateAsync(gameId, s =>
        {
            var now = DateTimeOffset.UtcNow;
            s.RunClock(now);
            s.ReleasedAtClock[challenge.Id] = s.ClockAt(now);
            s.CurrentChallengeId = challenge.Id;
            s.Withdrawn.Remove(challenge.Id);
            if (s.Layout == "board" && !s.Ended)
                s.Layout = "split";
            return true;
        }, token);

        await BroadcastAsync(new StageEventModel
        {
            Type = "released",
            GameId = gameId,
            ChallengeId = challenge.Id,
            ChallengeTitle = challenge.Title,
            Points = challenge.OriginalScore
        });
    }

    /// <summary>
    /// Records that the admin withdrew a challenge with no winner (it was already disabled through
    /// the challenge edit API).
    /// </summary>
    public async Task<DuelStateModel> MarkWithdrawnAsync(int gameId, int challengeId,
        CancellationToken token = default)
    {
        var changed = false;
        int? seconds = null;

        var state = await UpdateAsync(gameId, s =>
        {
            if (s.Withdrawn.Contains(challengeId))
                return false;

            seconds = s.LiveSecondsOf(challengeId, DateTimeOffset.UtcNow);
            s.Withdrawn.Add(challengeId);
            if (s.CurrentChallengeId == challengeId)
                s.CurrentChallengeId = null;
            changed = true;
            return true;
        }, token);

        if (!changed)
            return state;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var title = await db.GameChallenges.AsNoTracking()
            .Where(c => c.Id == challengeId && c.GameId == gameId)
            .Select(c => c.Title)
            .FirstOrDefaultAsync(token);

        await BroadcastAsync(new StageEventModel
        {
            Type = "withdrawn",
            GameId = gameId,
            ChallengeId = challengeId,
            ChallengeTitle = title,
            Seconds = seconds
        });

        return state;
    }

    /// <summary>
    /// "Start match": stamps the start and plays the 3-2-1 countdown on the stage. The admin page
    /// releases the first challenge when the countdown ends.
    /// </summary>
    public async Task<DuelStateModel> StartMatchAsync(int gameId, CancellationToken token = default)
    {
        var started = false;
        var state = await UpdateAsync(gameId, s =>
        {
            if (!s.Enabled || s.Ended || s.Paused)
                return false;

            s.StartedUtc ??= DateTimeOffset.UtcNow;
            if (s.Layout == "board")
                s.Layout = "split";
            started = true;
            return true;
        }, token);

        if (started)
            await BroadcastAsync(new StageEventModel { Type = "countdown", GameId = gameId });

        return state;
    }

    /// <summary>
    /// Tag-in: puts a team member on the keyboard. The previous player's stream is closed and
    /// their browser shows the bench; the projector keeps the team's panel and switches to the
    /// new player's screen as soon as they share it. A null user clears the choice.
    /// </summary>
    /// <returns>The new state, or an error message.</returns>
    public async Task<(DuelStateModel? State, string? Error)> SetOperatorAsync(int gameId, int participationId,
        Guid? userId, CancellationToken token = default)
    {
        string teamName;
        string? userName = null;

        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var team = await db.Participations.AsNoTracking()
                .Where(p => p.Id == participationId && p.GameId == gameId)
                .Select(p => new { p.Team.Name })
                .FirstOrDefaultAsync(token);

            if (team is null)
                return (null, "That team is not in this match.");

            teamName = team.Name;

            if (userId is { } id)
            {
                userName = await db.UserParticipations.AsNoTracking()
                    .Where(up => up.ParticipationId == participationId && up.UserId == id)
                    .Select(up => up.User.UserName)
                    .FirstOrDefaultAsync(token);

                if (userName is null)
                    return (null,
                        "That player has not joined this match with the team. They must sign in and join the game first.");
            }
        }

        var liveBefore = _relay.PublisherOf(gameId, participationId);
        Guid? previous = null;
        var changed = false;
        var state = await UpdateAsync(gameId, s =>
        {
            previous = s.Operators.TryGetValue(participationId, out var current) ? current : null;
            if (previous == userId)
                return false;

            if (userId is { } id)
                s.Operators[participationId] = id;
            else
                s.Operators.Remove(participationId);

            changed = true;
            return true;
        }, token);

        if (userId is { } keep)
            await _relay.BenchAsync(gameId, participationId, keep);

        if (changed && userId is not null)
        {
            var before = previous is { } prev ? await UserNameOfAsync(prev, token) : liveBefore?.UserName;

            await BroadcastAsync(new StageEventModel
            {
                Type = "tagged",
                GameId = gameId,
                ParticipationId = participationId,
                TeamName = teamName,
                UserName = userName,
                Message = before is { Length: > 0 } && before != userName ? before : null
            });
        }

        return (state, null);
    }

    /// <summary>
    /// Tag-team rule: who may stream and submit for this team. Returns null when
    /// <paramref name="userId" /> may play, otherwise the name of the teammate on the keyboard
    /// (empty when unknown).
    /// </summary>
    public async Task<string?> BenchedByAsync(int gameId, int participationId, Guid userId,
        CancellationToken token = default)
    {
        var state = await GetAsync(gameId, token);

        if (state.Operators.TryGetValue(participationId, out var op))
            return op == userId ? null : await UserNameOfAsync(op, token) ?? string.Empty;

        // Nobody tagged in: the member already streaming holds the keyboard until the monitor
        // tags someone else in or their stream ends.
        var publisher = _relay.PublisherOf(gameId, participationId);
        return publisher is { } live && live.UserId != userId ? live.UserName : null;
    }

    private async Task<string?> UserNameOfAsync(Guid userId, CancellationToken token)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.UserName)
            .FirstOrDefaultAsync(token);
    }

    /// <summary>
    /// Called by the flag checker after a submission in any game has been judged.
    /// </summary>
    public async Task OnSubmissionCheckedAsync(Submission item, AnswerResult answer, IServiceProvider services,
        CancellationToken token)
    {
        if (!IsDuelGame(item.GameId) || answer == AnswerResult.NotFound)
            return;

        try
        {
            var db = services.GetRequiredService<AppDbContext>();
            var challenge = await db.GameChallenges
                .FirstOrDefaultAsync(c => c.Id == item.ChallengeId && c.GameId == item.GameId, token);

            var accepted = answer == AnswerResult.Accepted;
            var liveSeconds = (await GetAsync(item.GameId, token)).LiveSecondsOf(item.ChallengeId, DateTimeOffset.UtcNow);

            // Only the participation that owns the first solve captured the challenge.
            if (accepted)
            {
                var firstSolver = await db.FirstSolves.AsNoTracking()
                    .Where(fs => fs.ChallengeId == item.ChallengeId)
                    .OrderBy(fs => fs.SubmissionId)
                    .Select(fs => (int?)fs.ParticipationId)
                    .FirstOrDefaultAsync(token);

                accepted = firstSolver is null || firstSolver == item.ParticipationId;
            }

            await BroadcastAsync(new StageEventModel
            {
                Type = accepted ? "accepted" : "denied",
                GameId = item.GameId,
                ParticipationId = item.ParticipationId,
                TeamName = item.TeamName,
                UserName = item.UserName,
                ChallengeId = item.ChallengeId,
                ChallengeTitle = challenge?.Title ?? item.ChallengeName,
                Points = accepted ? challenge?.OriginalScore : null,
                Seconds = accepted ? liveSeconds : null
            });

            if (!accepted || challenge is null)
                return;

            // S2 — a capture within seconds of the release, on screen, is either a prepared flag or
            // a leak; the SOC gets it for review (the projector already showed it to everyone).
            if (liveSeconds is { } secs && secs < 20)
                _signals.Signal($"ZarrarFastCapture:{item.GameId}:{item.ChallengeId}",
                    new BURAQ.Services.Soc.SocEventMessage(
                        EventType: "ZarrarFastCapture",
                        Category: SocCategory.AntiCheat,
                        Severity: SocSeverity.Medium,
                        Details: $"A showdown challenge was captured {secs} s after it was released.",
                        Source: nameof(ZarrarDuelService),
                        TeamId: item.TeamId,
                        UserId: item.UserId,
                        GameId: item.GameId,
                        ChallengeId: item.ChallengeId,
                        SubmissionId: item.Id,
                        PayloadJson: JsonSerializer.Serialize(new { seconds = secs, challenge = challenge.Title })));

            // The challenge is over for both players: tear down every instance of it.
            if (challenge.Type.IsContainer())
            {
                var instances = services.GetRequiredService<IGameInstanceRepository>();
                await instances.DestroyAllContainers(challenge, token);
            }

            var cache = services.GetRequiredService<CacheHelper>();
            await cache.FlushScoreboardCache(item.GameId, token);

            // The target is done: clear it, which also pushes the state to both players so their
            // challenge lists refresh and the captured challenge vanishes.
            var capturedId = challenge.Id;
            await UpdateAsync(item.GameId, s =>
            {
                if (s.CurrentChallengeId == capturedId)
                    s.CurrentChallengeId = null;
                return true;
            }, token);

            await BroadcastAsync(new StageEventModel
            {
                Type = "captured",
                GameId = item.GameId,
                ParticipationId = item.ParticipationId,
                TeamName = item.TeamName,
                UserName = item.UserName,
                ChallengeId = challenge.Id,
                ChallengeTitle = challenge.Title,
                Points = challenge.OriginalScore,
                Seconds = liveSeconds
            });
        }
        catch (Exception e)
        {
            _logger.LogErrorMessage(e, "ZARRAR could not process a judged submission");
        }
    }

    /// <summary>
    /// True when the challenge has already been captured in this game.
    /// </summary>
    public static Task<bool> IsCapturedAsync(AppDbContext db, int challengeId, CancellationToken token) =>
        db.FirstSolves.AsNoTracking().AnyAsync(fs => fs.ChallengeId == challengeId, token);

    /// <summary>
    /// Hides captured challenges from a player's challenge list.
    /// </summary>
    public static Dictionary<ChallengeCategory, IEnumerable<ChallengeInfo>> HideCaptured(
        Dictionary<ChallengeCategory, IEnumerable<ChallengeInfo>> challenges)
    {
        var result = new Dictionary<ChallengeCategory, IEnumerable<ChallengeInfo>>();

        foreach (var (category, list) in challenges)
        {
            var open = list.Where(c => c.SolvedCount == 0).ToArray();
            if (open.Length > 0)
                result[category] = open;
        }

        return result;
    }

    /// <summary>
    /// Everything the stage and the admin panel need in one call.
    /// </summary>
    /// <param name="gameId">The game the match belongs to.</param>
    /// <param name="token">Cancellation token.</param>
    /// <param name="forAdmin">Also build the pre-flight checklist.</param>
    public async Task<DuelSnapshotModel> BuildSnapshotAsync(int gameId, CancellationToken token = default,
        bool forAdmin = false)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var games = scope.ServiceProvider.GetRequiredService<IGameRepository>();

        var game = await games.GetGameById(gameId, token);
        if (game is null)
            return new DuelSnapshotModel { GameId = gameId };

        var state = await GetAsync(gameId, token);
        var scoreboard = await games.GetScoreboard(game, token);

        var participations = await db.Participations.AsNoTracking()
            .Where(p => p.GameId == gameId && p.Status == ParticipationStatus.Accepted)
            .OrderBy(p => p.Id)
            .Select(p => new { p.Id, p.TeamId, TeamName = p.Team.Name, p.Team.AvatarHash })
            .ToListAsync(token);

        var partIds = participations.Select(p => p.Id).ToArray();
        var members = await db.UserParticipations.AsNoTracking()
            .Where(up => partIds.Contains(up.ParticipationId))
            .Select(up => new { up.ParticipationId, up.UserId, up.User.UserName, up.User.AvatarHash })
            .ToListAsync(token);

        var attempts = await db.Submissions.AsNoTracking()
            .Where(sub => sub.GameId == gameId && partIds.Contains(sub.ParticipationId))
            .GroupBy(sub => sub.ParticipationId)
            .Select(g => new { ParticipationId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ParticipationId, x => x.Count, token);

        var players = participations.Select((p, index) =>
        {
            scoreboard.Items.TryGetValue(p.TeamId, out var item);

            var roster = members.Where(m => m.ParticipationId == p.Id && m.UserName != null)
                .OrderBy(m => m.UserName, StringComparer.OrdinalIgnoreCase)
                .Select(m => new DuelMemberModel
                {
                    UserId = m.UserId,
                    UserName = m.UserName!,
                    Avatar = m.AvatarHash is null ? null : $"/assets/{m.AvatarHash}/avatar"
                })
                .ToArray();

            Guid? operatorId = state.Operators.TryGetValue(p.Id, out var op) ? op : null;
            var publisher = _relay.PublisherOf(gameId, p.Id);
            var operatorName = operatorId is { } oid
                ? roster.FirstOrDefault(m => m.UserId == oid)?.UserName
                : publisher?.UserName;

            return new DuelPlayerModel
            {
                Slot = index == 0 ? "p1" : index == 1 ? "p2" : $"p{index + 1}",
                ParticipationId = p.Id,
                TeamId = p.TeamId,
                TeamName = p.TeamName,
                Avatar = p.AvatarHash is null ? null : $"/assets/{p.AvatarHash}/avatar",
                Members = members.Where(m => m.ParticipationId == p.Id && m.UserName != null)
                    .Select(m => m.UserName!).ToArray(),
                Score = item?.Score ?? 0,
                Rank = item?.Rank ?? 0,
                SolvedCount = item?.SolvedCount ?? 0,
                Attempts = attempts.GetValueOrDefault(p.Id),
                Live = _relay.IsLive(gameId, p.Id),
                Roster = roster,
                OperatorId = operatorId,
                LiveUserId = publisher?.UserId,
                OperatorName = operatorName
            };
        }).ToArray();

        var challenges = await db.GameChallenges.AsNoTracking()
            .Where(c => c.GameId == gameId)
            .OrderBy(c => c.Id)
            .Select(c => new { c.Id, c.Title, c.Category, c.OriginalScore, c.IsEnabled, c.DisableBloodBonus })
            .ToListAsync(token);

        var solves = await db.FirstSolves.AsNoTracking()
            .Where(fs => fs.Challenge.GameId == gameId)
            .Select(fs => new { fs.ChallengeId, fs.ParticipationId, fs.SubmissionId, fs.Submission.SubmitTimeUtc })
            .ToListAsync(token);

        var firstByChallenge = solves
            .GroupBy(s => s.ChallengeId)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.SubmissionId).First());

        var nameByPart = participations.ToDictionary(p => p.Id, p => p.TeamName);

        DuelReadinessModel[]? readiness = null;
        if (forAdmin)
        {
            var pending = await db.Participations.AsNoTracking()
                .CountAsync(p => p.GameId == gameId && p.Status == ParticipationStatus.Pending, token);

            var visibleEarly = challenges
                .Where(c => c.IsEnabled && !firstByChallenge.ContainsKey(c.Id))
                .Select(c => c.Title)
                .ToArray();

            var bloodPermille = (game.BloodBonusValue >> 20) & 0x3ff;
            var bonusChallenges = bloodPermille > 0 ? challenges.Count(c => !c.DisableBloodBonus) : 0;

            // S4 — the SOC must be working before a match: its backlog, dead letters and failures,
            // and whether staff accounts are protected by two-step login.
            var now = DateTimeOffset.UtcNow;
            var dayAgo = now.AddDays(-1);
            var socHealth = new SocHealth(
                await db.SocEventOutbox.AsNoTracking()
                    .CountAsync(x => x.ProcessedAtUtc == null && x.AttemptCount >= 10, token),
                await db.SocDetectionJobs.AsNoTracking()
                    .CountAsync(x => x.Status == SocDetectionStatus.Failed && x.CompletedAtUtc >= dayAgo, token),
                await db.SocDetectionJobs.AsNoTracking()
                    .Where(x => x.Status == SocDetectionStatus.Pending)
                    .MinAsync(x => (DateTimeOffset?)x.CreatedAtUtc, token),
                await db.Users.AsNoTracking()
                    .Where(u => u.Role >= Role.Monitor && !u.TwoFactorEnabled)
                    .Select(u => u.UserName!)
                    .Take(10)
                    .ToArrayAsync(token));

            readiness = BuildReadiness(game, state, players, challenges.Count, visibleEarly, pending,
                bloodPermille, bonusChallenges, ZarrarHub.ScreenCount(gameId), socHealth);
        }

        return new DuelSnapshotModel
        {
            GameId = gameId,
            Title = game.Title,
            StartUtc = game.StartTimeUtc,
            EndUtc = game.EndTimeUtc,
            ServerTimeUtc = DateTimeOffset.UtcNow,
            Duel = state,
            Players = players,
            StageScreens = ZarrarHub.ScreenCount(gameId),
            Readiness = readiness,
            Challenges = challenges.Select(c =>
            {
                firstByChallenge.TryGetValue(c.Id, out var first);
                return new DuelChallengeModel
                {
                    Id = c.Id,
                    Title = c.Title,
                    Category = c.Category.ToString(),
                    Score = c.OriginalScore,
                    Status = first is not null ? "captured"
                        : c.IsEnabled ? "live"
                        : state.Withdrawn.Contains(c.Id) ? "withdrawn"
                        : "queued",
                    CapturedBy = first?.ParticipationId,
                    CapturedByName = first is not null && nameByPart.TryGetValue(first.ParticipationId, out var n)
                        ? n
                        : null,
                    CapturedAtUtc = first?.SubmitTimeUtc
                };
            }).ToArray()
        };
    }

    /// <summary>
    /// The admin's pre-flight checklist. Critical items block "Start match" and "Release" in the
    /// admin page (with an override); the rest are advice.
    /// </summary>
    private static DuelReadinessModel[] BuildReadiness(global::BURAQ.Models.Data.Game game, DuelStateModel state, DuelPlayerModel[] players,
        int challengeCount, string[] visibleEarly, int pending, long bloodPermille, int bonusChallenges,
        int screens, SocHealth soc)
    {
        var now = DateTimeOffset.UtcNow;
        var items = new List<DuelReadinessModel>();

        void Add(string key, string label, bool ok, bool critical, string detail) =>
            items.Add(new DuelReadinessModel
            {
                Key = key, Label = label, Ok = ok, Critical = critical, Detail = detail
            });

        Add("mode", "Showdown mode on", state.Enabled, true,
            state.Enabled
                ? "Screen sharing is required and the first flag captures."
                : "Turn on \"1v1 showdown mode\" below.");

        var playerDetail = players.Length switch
        {
            2 => $"{players[0].TeamName} vs {players[1].TeamName}.",
            0 => "No accepted players yet.",
            1 => $"Only {players[0].TeamName} is accepted.",
            _ => $"{players.Length} players are accepted; a showdown needs exactly two."
        };
        if (players.Length != 2 && pending > 0)
            playerDetail += $" {pending} waiting in the game's Review tab.";
        Add("players", "Exactly two players", players.Length == 2, true, playerDetail);

        var notSharing = players.Where(p => !p.Live).Select(p => p.TeamName).ToArray();
        Add("feeds", "Both screens live", players.Length == 2 && notSharing.Length == 0, true,
            players.Length == 0
                ? "Waiting for players."
                : notSharing.Length == 0
                    ? "Both players are sharing their entire screen."
                    : $"Not sharing yet: {string.Join(", ", notSharing)}. They open the challenges page and click \"Share entire screen\".");

        string windowDetail;
        bool windowOk;
        if (state.Ended)
        {
            windowOk = true;
            windowDetail = "The match has been ended.";
        }
        else if (now < game.StartTimeUtc)
        {
            windowOk = false;
            var wait = game.StartTimeUtc - now;
            windowDetail = $"The game starts in {FormatSpan(wait)}. Players cannot open challenges before then. Move the start time to now in the game's Info tab, or wait.";
        }
        else if (now >= game.EndTimeUtc)
        {
            windowOk = false;
            windowDetail = "The game's end time has passed, so flags are refused. Move the end time later in the game's Info tab.";
        }
        else
        {
            windowOk = true;
            windowDetail = $"Open now; {FormatSpan(game.EndTimeUtc - now)} left on the game clock.";
        }
        Add("window", "Game is open", windowOk, true, windowDetail);

        Add("challenges", "Challenges added", challengeCount > 0, true,
            challengeCount > 0
                ? $"{challengeCount} challenge{(challengeCount == 1 ? "" : "s")}; released top to bottom."
                : "Add challenges in the game's Challenges tab.");

        var preRelease = state.ReleasedAtClock.Count == 0 && state.StartedUtc is null;
        if (preRelease)
            Add("hidden", "Challenges hidden until released", visibleEarly.Length == 0, false,
                visibleEarly.Length == 0
                    ? "Every challenge is disabled; Release turns them on one by one."
                    : $"Already visible to players: {string.Join(", ", visibleEarly.Take(5))}{(visibleEarly.Length > 5 ? "…" : "")}. Disable them in the Challenges tab so they appear only when released.");

        Add("screen", "Projector stage open", screens > 0, false,
            screens > 0
                ? $"{screens} monitor stage{(screens == 1 ? " is" : "s are")} showing this match."
                : "No monitor stage is open. Log in as the monitor on the projector laptop and open this match.");

        // Tag-team: a team may bring several players, but only the one on the keyboard streams and
        // submits. A multi-player team should have someone tagged in so nobody can grab the stream.
        var untagged = players.Where(p => p.Roster.Length > 1 && p.OperatorId is null)
            .Select(p => p.TeamName).ToArray();
        var keyboards = players.Select(p =>
                $"{p.TeamName}: {p.OperatorName ?? "nobody yet"}{(p.Roster.Length > 1 ? $" ({p.Roster.Length} in roster)" : "")}")
            .ToArray();
        Add("teamSize", "Player on the keyboard per team", untagged.Length == 0, false,
            untagged.Length > 0
                ? $"Tag a player in for {string.Join(", ", untagged)} (TAG IN on the monitor or LIVE page). Until then whichever member shares first plays."
                : keyboards.Length > 0
                    ? string.Join(" · ", keyboards)
                    : "No players yet.");

        Add("practice", "Practice mode off", !game.PracticeMode, false,
            game.PracticeMode
                ? "Practice mode is on, so players can keep submitting after the match ends. Turn it off in the game's Info tab."
                : "Nothing is accepted after the end.");

        Add("scoring", "Points match what is announced", bonusChallenges == 0, false,
            bonusChallenges == 0
                ? "Each capture scores exactly the listed points."
                : $"The first-blood bonus is on for {bonusChallenges} challenge{(bonusChallenges == 1 ? "" : "s")}, so every capture scores {bloodPermille / 10.0:0.#}% more than the listed points. Turn the bonus off in each challenge's settings, or set it to 0 in the game's Info tab.");

        var backlog = soc.OldestPendingJob is { } oldestJob ? now - oldestJob : TimeSpan.Zero;
        var socOk = soc.DeadLetters == 0 && soc.FailedJobs == 0 && backlog < TimeSpan.FromMinutes(2);
        Add("soc", "SOC pipeline healthy", socOk, false,
            socOk
                ? "Detection and the event timeline are keeping up."
                : string.Join(" ", new[]
                {
                    soc.DeadLetters > 0 ? $"{soc.DeadLetters} SOC event(s) dead-lettered." : null,
                    soc.FailedJobs > 0 ? $"{soc.FailedJobs} detection job(s) failed today." : null,
                    backlog >= TimeSpan.FromMinutes(2) ? $"Detection is {FormatSpan(backlog)} behind." : null,
                    "See Administration → SOC → Queues."
                }.Where(x => x is not null)));

        Add("staff2fa", "Staff use two-step login", soc.StaffWithoutTwoFactor.Length == 0, false,
            soc.StaffWithoutTwoFactor.Length == 0
                ? "Every Monitor and Admin account has two-step login."
                : $"No two-step login yet: {string.Join(", ", soc.StaffWithoutTwoFactor)}. Turn it on in Settings → Two-step login on each staff account.");

        return [.. items];
    }

    /// <summary>SOC and staff-account facts for the pre-flight checklist.</summary>
    private sealed record SocHealth(int DeadLetters, int FailedJobs, DateTimeOffset? OldestPendingJob,
        string[] StaffWithoutTwoFactor);

    private static string FormatSpan(TimeSpan span)
    {
        if (span.TotalDays >= 1)
            return $"{(int)span.TotalDays}d {span.Hours}h";
        if (span.TotalHours >= 1)
            return $"{(int)span.TotalHours}h {span.Minutes}m";
        if (span.TotalMinutes >= 1)
            return $"{(int)span.TotalMinutes} min";
        return $"{Math.Max(0, (int)span.TotalSeconds)} s";
    }

    /// <summary>
    /// Lists games for the monitor's event picker.
    /// </summary>
    public async Task<DuelGameModel[]> ListGamesAsync(bool includeHidden, CancellationToken token = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var games = await db.Games.AsNoTracking()
            .Where(g => includeHidden || !g.Hidden)
            .OrderByDescending(g => g.StartTimeUtc)
            .Take(50)
            .Select(g => new
            {
                g.Id,
                g.Title,
                g.StartTimeUtc,
                g.EndTimeUtc,
                Players = g.Participations.Count(p => p.Status == ParticipationStatus.Accepted)
            })
            .ToListAsync(token);

        var result = new List<DuelGameModel>(games.Count);
        foreach (var g in games)
        {
            var state = await GetAsync(g.Id, token);
            result.Add(new DuelGameModel
            {
                Id = g.Id,
                Title = g.Title,
                StartUtc = g.StartTimeUtc,
                EndUtc = g.EndTimeUtc,
                Duel = state.Enabled,
                Paused = state.Paused,
                Ended = state.Ended,
                Players = g.Players
            });
        }

        // Showdowns first, then the rest by start time.
        return result.OrderByDescending(g => g.Duel).ThenByDescending(g => g.StartUtc).ToArray();
    }

    private async Task OnFeedChanged(FeedChange change)
    {
        if (!IsDuelGame(change.GameId))
            return;

        if (change.Live)
        {
            await BroadcastAsync(new StageEventModel
            {
                Type = "feedRestored",
                GameId = change.GameId,
                ParticipationId = change.ParticipationId,
                TeamName = change.TeamName,
                UserName = change.UserName
            });
            return;
        }

        // Give a reconnecting browser a moment before stopping the match.
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(FeedLossGrace);

                if (_relay.IsLive(change.GameId, change.ParticipationId))
                    return;

                // A tag-out is a swap, not a lost feed: the benched player's stream ended because
                // a teammate was tagged in. The team cannot submit until the new player streams.
                var current = await GetAsync(change.GameId);
                if (change.UserId is { } who && current.Operators.TryGetValue(change.ParticipationId, out var op)
                                             && op != who)
                    return;

                await BroadcastAsync(new StageEventModel
                {
                    Type = "feedLost",
                    GameId = change.GameId,
                    ParticipationId = change.ParticipationId,
                    TeamName = change.TeamName,
                    UserName = change.UserName
                });

                // A player whose stream keeps dropping may be dodging the camera: the SOC counts it
                // (the sink folds repeats per player, and the payload carries the running total).
                _signals.Signal($"ZarrarFeedLost:{change.GameId}:{change.ParticipationId}:{change.UserId}",
                    new BURAQ.Services.Soc.SocEventMessage(
                        EventType: "ZarrarFeedLost",
                        Category: SocCategory.AntiCheat,
                        Severity: SocSeverity.Low,
                        Details: "A showdown player's screen stream dropped for longer than the grace period.",
                        Source: nameof(ZarrarDuelService),
                        UserId: change.UserId,
                        GameId: change.GameId,
                        PayloadJson: JsonSerializer.Serialize(new
                        {
                            participationId = change.ParticipationId,
                            team = change.TeamName,
                            player = change.UserName
                        })));

                if (!await IsRunningAsync(change.GameId))
                    return;

                await PauseAsync(change.GameId, $"FEED LOST: {change.TeamName}");
            }
            catch (Exception e)
            {
                _logger.LogErrorMessage(e, "ZARRAR feed-loss handling failed");
            }
        });
    }

    /// <summary>
    /// The match clock is running: the game has started, has not ended, and was not ended by the admin.
    /// </summary>
    private async Task<bool> IsRunningAsync(int gameId)
    {
        var state = await GetAsync(gameId);
        if (!state.Enabled || state.Ended || state.Paused)
            return false;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;

        return await db.Games.AsNoTracking()
            .AnyAsync(g => g.Id == gameId && g.StartTimeUtc <= now && g.EndTimeUtc > now);
    }

    private static DuelStateModel? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.DuelStateModel);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
