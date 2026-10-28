using BURAQ.Models.Internal;

namespace BURAQ.Repositories.Interface;

public interface IGameInstanceRepository : IRepository
{
    /// <summary>
    /// Get or create a game instance for a team and challenge
    /// </summary>
    /// <param name="team">Team</param>
    /// <param name="challengeId">Challenge id</param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task<GameInstance?> GetInstance(Participation team, int challengeId, CancellationToken token = default);

    /// <summary>
    /// Get every instance for a participation that currently has a running container.
    /// </summary>
    /// <remarks>
    /// Instances are shared per team — keyed by (ParticipationId, ChallengeId) — so this is the
    /// team's live container set, not one player's. Used by the player-facing MyInstances
    /// endpoint to show what the team has running against its concurrency limit.
    /// </remarks>
    /// <param name="team">Participation</param>
    /// <param name="token"></param>
    public Task<GameInstance[]> GetRunningInstances(Participation team, CancellationToken token = default);

    /// <summary>
    /// Get a game instance for submission, with minimal includes
    /// </summary>
    /// <param name="team">Team</param>
    /// <param name="challengeId">Challenge id</param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task<GameInstance?> GetInstanceForSubmission(Participation team, int challengeId,
        CancellationToken token = default);

    /// <summary>
    /// Verify the answer of a submission
    /// </summary>
    /// <param name="submission">Submission</param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task<VerifyResult> VerifyAnswer(Submission submission, CancellationToken token = default);

    /// <summary>
    /// Check if the submission is a cheat
    /// </summary>
    /// <param name="submission">Submission</param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task<CheatCheckInfo> CheckCheat(Submission submission, CancellationToken token = default);

    /// <summary>
    /// Create a container for a game instance
    /// </summary>
    /// <param name="team">Team info</param>
    /// <param name="game">Game</param>
    /// <param name="user">User</param>
    /// <param name="gameInstance">Instance</param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task<TaskResult<Container>> CreateContainer(GameInstance gameInstance, Team team, UserInfo user,
        Game game, CancellationToken token = default);

    /// <summary>
    /// Destroy all containers of a challenge
    /// </summary>
    /// <param name="challenge">Challenge</param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task<bool> DestroyAllContainers(GameChallenge challenge, CancellationToken token = default);
}
