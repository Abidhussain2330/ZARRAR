using ZARRAR.Models.Data;
using ZARRAR.Utils;

namespace ZARRAR.Services;

/// <summary>
/// Reconciles a team's roster with its participation in a game.
/// </summary>
public static class TeamParticipationEnrollment
{
    /// <summary>
    /// Ensures the participation is accepted and includes every current team member.
    /// </summary>
    public static Participation Reconcile(Game game, Team team, Participation? existing,
        Func<Game, Team, string> tokenFactory)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(tokenFactory);

        var participation = existing ?? new Participation
        {
            Game = game,
            GameId = game.Id,
            Team = team,
            TeamId = team.Id,
            Token = tokenFactory(game, team)
        };

        participation.Game = game;
        participation.GameId = game.Id;
        participation.Team = team;
        participation.TeamId = team.Id;

        var enrolledUserIds = participation.Members.Select(member => member.UserId).ToHashSet();
        foreach (var member in team.Members.Where(member => enrolledUserIds.Add(member.Id)))
        {
            participation.Members.Add(new UserParticipation
            {
                Participation = participation,
                User = member,
                UserId = member.Id,
                Game = game,
                GameId = game.Id,
                Team = team,
                TeamId = team.Id
            });
        }

        participation.Status = ParticipationStatus.Accepted;
        team.Locked = true;

        return participation;
    }
}
