using System.Reflection;
using BURAQ.Controllers;
using BURAQ.Models;
using BURAQ.Models.Data;
using BURAQ.Utils;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace BURAQ.Test;

public sealed class TeamParticipationEnrollmentTests
{
    [Fact]
    public void Reconcile_creates_an_accepted_participation_with_every_team_member()
    {
        var game = new Game { Id = 7 };
        var members = CreateMembers(3);
        var team = new Team { Id = 11, Members = members.ToHashSet() };
        var tokenFactoryCalls = 0;
        Func<Game, Team, string> tokenFactory = (_, _) =>
        {
            tokenFactoryCalls++;
            return "team-token";
        };

        var participation = Reconcile(game, team, null, tokenFactory);

        Assert.Equal(1, tokenFactoryCalls);
        Assert.Equal("team-token", participation.Token);
        Assert.Equal(ParticipationStatus.Accepted, participation.Status);
        Assert.True(team.Locked);
        Assert.Equal(team.Id, participation.TeamId);
        Assert.Equal(game.Id, participation.GameId);
        Assert.Equal(members.Select(member => member.Id).Order(),
            participation.Members.Select(member => member.UserId).Order());
        Assert.All(participation.Members, member =>
        {
            Assert.Equal(team.Id, member.TeamId);
            Assert.Equal(game.Id, member.GameId);
            Assert.Same(participation, member.Participation);
        });
    }

    [Fact]
    public void Reconcile_restores_missing_members_without_replacing_an_existing_token()
    {
        var game = new Game { Id = 3 };
        var members = CreateMembers(2);
        var team = new Team { Id = 5, Members = members.ToHashSet() };
        var existing = new Participation
        {
            Game = game,
            GameId = game.Id,
            Team = team,
            TeamId = team.Id,
            Token = "existing-token",
            Status = ParticipationStatus.Pending,
            Members =
            [
                new UserParticipation
                {
                    User = members[0],
                    UserId = members[0].Id,
                    Game = game,
                    GameId = game.Id,
                    Team = team,
                    TeamId = team.Id
                }
            ]
        };

        var participation = Reconcile(game, team, existing, (_, _) => throw new InvalidOperationException());

        Assert.Same(existing, participation);
        Assert.Equal("existing-token", participation.Token);
        Assert.Equal(ParticipationStatus.Accepted, participation.Status);
        Assert.Equal(members.Select(member => member.Id).Order(),
            participation.Members.Select(member => member.UserId).Order());
    }

    [Fact]
    public void Admin_controller_exposes_the_bulk_team_enrollment_operation()
    {
        var method = typeof(AdminController).GetMethod("EnrollAllTeams");

        Assert.NotNull(method);
        var route = method!.GetCustomAttribute<HttpPostAttribute>();
        Assert.NotNull(route);
        Assert.Equal("Games/{id:int}/EnrollAllTeams", route!.Template);
    }

    private static Participation Reconcile(Game game, Team team, Participation? existing,
        Func<Game, Team, string> tokenFactory)
    {
        var enrollmentType = typeof(AppDbContext).Assembly.GetType("BURAQ.Services.TeamParticipationEnrollment");
        Assert.NotNull(enrollmentType);

        var method = enrollmentType!.GetMethod("Reconcile", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);

        return Assert.IsType<Participation>(method!.Invoke(null, [game, team, existing, tokenFactory]));
    }

    private static List<UserInfo> CreateMembers(int count) =>
        Enumerable.Range(0, count).Select(index => new UserInfo
        {
            Id = Guid.NewGuid(),
            UserName = $"member-{index}",
            Email = $"member-{index}@example.test"
        }).ToList();
}
