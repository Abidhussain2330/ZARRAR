using BURAQ.Models;
using BURAQ.Models.Request.Dashboard;
using BURAQ.Repositories.Interface;
using BURAQ.Services.Cache;
using Microsoft.EntityFrameworkCore;

namespace BURAQ.Repositories;

/// <summary>
/// Aggregate queries backing the participant command centre dashboard.
/// </summary>
/// <remarks>
/// The two platform-wide aggregates scan large tables, so both are cached the same way the
/// scoreboard is, through <see cref="CacheHelper" />. The cache key carries the visibility scope,
/// so a privileged response — which may include hidden games — is never handed to an
/// unprivileged caller out of the cache.
/// </remarks>
public class DashboardRepository(
    ILogger<DashboardRepository> logger,
    CacheHelper cacheHelper,
    AppDbContext context) : RepositoryBase(context), IDashboardRepository
{
    /// <summary>
    /// How long the platform counters stay cached.
    /// </summary>
    private static readonly TimeSpan OverviewCacheDuration = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long the live solve feed stays cached. Short, because the feed is the one part of the
    /// dashboard participants watch during a running competition.
    /// </summary>
    private static readonly TimeSpan LiveSolveCacheDuration = TimeSpan.FromSeconds(15);

    public Task<DashboardOverviewModel> GetOverview(bool includeHidden, CancellationToken token = default) =>
        cacheHelper.GetOrCreateAsync(logger, CacheKey.DashboardOverview(includeHidden),
            entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = OverviewCacheDuration;
                return FetchOverview(includeHidden, token);
            }, token: token);

    public async Task<LiveSolveModel[]> GetLiveSolves(bool includeHidden, int count,
        CancellationToken token = default)
    {
        // The cap is applied here as well as at the model binder, so no caller — including any
        // future internal one that bypasses the controller — can ask for an unbounded feed.
        var requested = Math.Clamp(count, 1, Limits.MaxDashboardLiveSolveCount);

        // Always cache the full window and slice it afterwards, so that callers asking for
        // different counts share a single cache entry instead of one entry per count.
        var solves = await cacheHelper.GetOrCreateAsync(logger, CacheKey.DashboardLiveSolves(includeHidden),
            entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = LiveSolveCacheDuration;
                return FetchLiveSolves(includeHidden, Limits.MaxDashboardLiveSolveCount, token);
            }, token: token);

        return solves.Length <= requested ? solves : solves[..requested];
    }

    public async Task<int?> GetFeaturedGameId(Guid userId, bool includeHidden, CancellationToken token = default)
    {
        var now = DateTimeOffset.UtcNow;
        var games = VisibleGames(includeHidden);

        // 1. An active game the user has joined. Ordered by end time so the competition that is
        //    about to close is surfaced ahead of one that runs for another month.
        var joined = await games
            .Where(g => g.StartTimeUtc <= now && now <= g.EndTimeUtc &&
                        Context.UserParticipations.Any(up => up.GameId == g.Id && up.UserId == userId))
            .OrderBy(g => g.EndTimeUtc)
            .Select(g => (int?)g.Id)
            .FirstOrDefaultAsync(token);

        if (joined is not null)
            return joined;

        // 2. Otherwise the soonest game that has not started yet.
        return await games
            .Where(g => g.StartTimeUtc > now)
            .OrderBy(g => g.StartTimeUtc)
            .Select(g => (int?)g.Id)
            .FirstOrDefaultAsync(token);
    }

    public Task<Game?> GetVisibleGame(int gameId, bool includeHidden, CancellationToken token = default) =>
        VisibleGames(includeHidden).FirstOrDefaultAsync(g => g.Id == gameId, token);

    /// <summary>
    /// The set of games the caller is allowed to see.
    /// </summary>
    /// <remarks>
    /// The predicate is applied by branching in C# rather than by folding the flag into the
    /// expression tree, so the unprivileged query is a plain <c>WHERE NOT "Hidden"</c> that the
    /// planner can reason about, instead of a parameterised disjunction.
    /// </remarks>
    private IQueryable<Game> VisibleGames(bool includeHidden)
    {
        IQueryable<Game> games = Context.Games.AsNoTracking();
        return includeHidden ? games : games.Where(g => !g.Hidden);
    }

    private async Task<DashboardOverviewModel> FetchOverview(bool includeHidden, CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow;
        var since = now - TimeSpan.FromHours(24);

        var games = VisibleGames(includeHidden);

        var activeGameCount = await games.CountAsync(g => g.StartTimeUtc <= now && now <= g.EndTimeUtc, token);
        var upcomingGameCount = await games.CountAsync(g => g.StartTimeUtc > now, token);

        // Only enabled challenges are ever counted: a challenge that is not enabled is not visible
        // to participants and must not show up in a public total either.
        var challenges = Context.GameChallenges.AsNoTracking().IgnoreAutoIncludes()
            .Where(c => c.IsEnabled);
        if (!includeHidden)
            challenges = challenges.Where(c => !c.Game.Hidden);
        var challengeCount = await challenges.CountAsync(token);

        var participations = Context.Participations.AsNoTracking().IgnoreAutoIncludes();
        if (!includeHidden)
            participations = participations.Where(p => !p.Game.Hidden);
        var teamCount = await participations.CountAsync(token);

        var members = Context.UserParticipations.AsNoTracking().IgnoreAutoIncludes();
        if (!includeHidden)
            members = members.Where(m => !m.Game.Hidden);
        var participantCount = await members.Select(m => m.UserId).Distinct().CountAsync(token);

        var solves = Context.Submissions.AsNoTracking().IgnoreAutoIncludes()
            .Where(s => s.Status == AnswerResult.Accepted &&
                        s.SubmitTimeUtc >= since &&
                        s.GameChallenge!.IsEnabled);
        if (!includeHidden)
            solves = solves.Where(s => !s.Game!.Hidden);
        var recentSolveCount = await solves.CountAsync(token);

        return new DashboardOverviewModel
        {
            ActiveGameCount = activeGameCount,
            UpcomingGameCount = upcomingGameCount,
            ChallengeCount = challengeCount,
            ParticipantCount = participantCount,
            TeamCount = teamCount,
            RecentSolveCount = recentSolveCount,
            UpdateTimeUtc = now
        };
    }

    private async Task<LiveSolveModel[]> FetchLiveSolves(bool includeHidden, int count, CancellationToken token)
    {
        var query = Context.Submissions.AsNoTracking().IgnoreAutoIncludes()
            .Where(s => s.Status == AnswerResult.Accepted && s.GameChallenge!.IsEnabled);

        if (!includeHidden)
            query = query.Where(s => !s.Game!.Hidden);

        // A single statement: the ordered top-N plus three correlated sub-selects over at most
        // Limits.MaxDashboardLiveSolveCount rows. Nothing here runs per result row on the client.
        var rows = await query
            .OrderByDescending(s => s.SubmitTimeUtc)
            .Take(count)
            .Select(s => new
            {
                SubmitTimeUtc = s.SubmitTimeUtc,
                UserName = s.User == null ? null : s.User.UserName,
                TeamName = s.Team!.Name,
                GameId = s.GameId,
                GameTitle = s.Game!.Title,
                ChallengeId = s.ChallengeId,
                ChallengeTitle = s.GameChallenge!.Title,
                Category = s.GameChallenge!.Category,
                OriginalScore = s.GameChallenge!.OriginalScore,
                MinScoreRate = s.GameChallenge!.MinScoreRate,
                Difficulty = s.GameChallenge!.Difficulty,
                FirstSolveCount = s.GameChallenge!.FirstSolves!.Count(),

                // A submission is a blood when it is itself a recorded first solve and no earlier
                // first solve exists for the same challenge. FirstSolves is indexed on ChallengeId
                // and uniquely on SubmissionId, so both sub-selects are index lookups.
                //
                // This is "first to solve it", which is what the feed claims. It is not the
                // scoreboard's blood: that one additionally requires the solving team's division
                // to hold GamePermission.GetBlood and the solve to fall inside the game window, so
                // in a game whose divisions withhold blood the two can name different teams.
                IsFirstBlood = Context.FirstSolves.Any(fs => fs.SubmissionId == s.Id) &&
                               !Context.FirstSolves.Any(fs => fs.ChallengeId == s.ChallengeId &&
                                                              fs.Submission.SubmitTimeUtc < s.SubmitTimeUtc)
            })
            .ToArrayAsync(token);

        return rows.Select(row => new LiveSolveModel
        {
            SubmitTimeUtc = row.SubmitTimeUtc,
            UserName = row.UserName,
            TeamName = row.TeamName,
            GameId = row.GameId,
            GameTitle = row.GameTitle,
            ChallengeId = row.ChallengeId,
            ChallengeTitle = row.ChallengeTitle,
            Category = row.Category,

            // Same formula and same inputs as GameChallenge.CurrentScore, so the feed shows the
            // score the challenge page shows rather than a second, divergent notion of score.
            Score = GameChallenge.CalculateChallengeScore(row.OriginalScore, row.MinScoreRate,
                row.Difficulty, row.FirstSolveCount),
            IsFirstBlood = row.IsFirstBlood
        }).ToArray();
    }
}
