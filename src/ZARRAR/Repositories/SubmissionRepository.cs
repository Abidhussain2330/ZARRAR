using ZARRAR.Extensions;
using ZARRAR.Hubs;
using ZARRAR.Hubs.Clients;
using ZARRAR.Models.Request.Game;
using ZARRAR.Repositories.Interface;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ZARRAR.Repositories;

public class SubmissionRepository(
    IHubContext<MonitorHub, IMonitorClient> hub,
    IConfiguration configuration,
    AppDbContext context) : RepositoryBase(context), ISubmissionRepository
{
    private readonly byte[] _fingerprintKey = configuration["XorKey"]?.ToUTF8Bytes() ?? [];

    public async Task<Submission> AddSubmission(Submission submission, CancellationToken token = default)
    {
        await Context.AddAsync(submission, token);
        await Context.SaveChangesAsync(token);

        return submission;
    }

    public Task<Submission?> GetSubmission(int gameId, int challengeId, Guid userId, int submitId,
        CancellationToken token = default)
        => Context.Submissions.IgnoreAutoIncludes().Where(s =>
                s.Id == submitId && s.UserId == userId && s.GameId == gameId && s.ChallengeId == challengeId)
            .SingleOrDefaultAsync(token);

    public Task<int> CountSubmissions(int participationId, int challengeId, CancellationToken token = default) =>
        Context.Submissions.CountAsync(s =>
            s.ParticipationId == participationId && s.ChallengeId == challengeId, token);

    // Same key space as the first-blood lock in GameInstanceRepository, so the limit check and the
    // first-solve check on one participation/challenge pair never interleave either.
    public Task AcquireSubmissionLock(int participationId, int challengeId, CancellationToken token = default) =>
        Context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0}, {1})",
            [participationId, challengeId], cancellationToken: token);

    public Task<Submission[]> GetUncheckedFlags(CancellationToken token = default) =>
        Context.Submissions.Where(s => s.Status == AnswerResult.FlagSubmitted)
            .AsNoTracking().Include(e => e.Game).ToArrayAsync(token);

    public Task<Submission[]> GetSubmissions(Game game, AnswerResult? type = null, int count = 100, int skip = 0,
        CancellationToken token = default) =>
        GetSubmissionsByType(type).Where(s => s.Game == game).TakeAllIfZero(count, skip).ToArrayAsync(token);

    public Task<Submission[]> GetSubmissions(GameChallenge challenge, AnswerResult? type = null, int count = 100,
        int skip = 0, CancellationToken token = default) =>
        GetSubmissionsByType(type).Where(s => s.GameChallenge == challenge).TakeAllIfZero(count, skip)
            .ToArrayAsync(token);

    public Task<Submission[]> GetSubmissions(Participation team, AnswerResult? type = null, int count = 100,
        int skip = 0, CancellationToken token = default) =>
        GetSubmissionsByType(type).Where(s => s.TeamId == team.TeamId).TakeAllIfZero(count, skip)
            .ToArrayAsync(token);

    public Task SendSubmission(Submission submission)
        => hub.Clients.Group($"Game_{submission.GameId}")
            .ReceivedSubmissions(SubmissionInfoModel.FromSubmission(submission, _fingerprintKey));


    private IQueryable<Submission> GetSubmissionsByType(AnswerResult? type = null)
    {
        var subs = type is not null
            ? Context.Submissions.Where(s => s.Status == type.Value)
            : Context.Submissions;

        return subs.OrderByDescending(s => s.SubmitTimeUtc);
    }
}
