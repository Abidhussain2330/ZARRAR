using ZARRAR.Controllers;
using ZARRAR.Models.Data;
using ZARRAR.Models.Request.Admin;
using ZARRAR.Models.Request.Info;
using ZARRAR.Repositories.Interface;
using ZARRAR.Services.Soc;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ZARRAR.Test;

public sealed class AdminTeamCreationBehaviorTests
{
    [Fact]
    public async Task Create_team_makes_the_selected_existing_user_captain_and_first_member()
    {
        var captain = new UserInfo { Id = Guid.CreateVersion7(), UserName = "captain" };
        using var userManager = CreateUserManager(captain);
        var teams = new RecordingTeamRepository();
        var socEvents = new RecordingSocEventPublisher();
        var controller = new AdminController(
            userManager,
            NullLogger<AdminController>.Instance,
            null!, null!, null!, null!, null!, null!, teams, null!,
            new ServiceCollection().BuildServiceProvider(), null!, socEvents, null!);

        var result = await controller.CreateTeam(new AdminTeamCreateModel
        {
            Name = "Blue Team",
            Bio = "Defenders",
            CaptainId = captain.Id
        });

        var ok = Assert.IsType<OkObjectResult>(result);
        var team = Assert.IsType<TeamInfoModel>(ok.Value);

        Assert.Equal("Blue Team", team.Name);
        Assert.Single(team.Members!);
        Assert.Equal(captain.Id, team.Members![0].Id);
        Assert.True(team.Members[0].Captain);
        Assert.NotNull(socEvents.Published);
        Assert.Equal("TeamCreatedByAdmin", socEvents.Published!.EventType);
        Assert.Equal(captain.Id, socEvents.Published.UserId);
    }

    private static UserManager<UserInfo> CreateUserManager(UserInfo user) =>
        new(
            new SingleUserStore(user),
            Options.Create(new IdentityOptions()),
            new PasswordHasher<UserInfo>(),
            [],
            [],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<UserManager<UserInfo>>.Instance);

    private sealed class SingleUserStore(UserInfo user) : IUserStore<UserInfo>
    {
        public void Dispose()
        {
        }

        public Task<IdentityResult> CreateAsync(UserInfo value, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> UpdateAsync(UserInfo value, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> DeleteAsync(UserInfo value, CancellationToken cancellationToken) =>
            Task.FromResult(IdentityResult.Success);

        public Task<UserInfo?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult(user.Id.ToString() == userId ? user : null);

        public Task<UserInfo?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
            Task.FromResult(string.Equals(user.NormalizedUserName, normalizedUserName, StringComparison.Ordinal) ? user : null);

        public Task<string> GetUserIdAsync(UserInfo value, CancellationToken cancellationToken) =>
            Task.FromResult(value.Id.ToString());

        public Task<string?> GetUserNameAsync(UserInfo value, CancellationToken cancellationToken) =>
            Task.FromResult(value.UserName);

        public Task SetUserNameAsync(UserInfo value, string? userName, CancellationToken cancellationToken)
        {
            value.UserName = userName;
            return Task.CompletedTask;
        }

        public Task<string?> GetNormalizedUserNameAsync(UserInfo value, CancellationToken cancellationToken) =>
            Task.FromResult(value.NormalizedUserName);

        public Task SetNormalizedUserNameAsync(UserInfo value, string? normalizedName, CancellationToken cancellationToken)
        {
            value.NormalizedUserName = normalizedName;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingTeamRepository : ITeamRepository
    {
        public Task<Team> CreateTeam(TeamUpdateModel model, UserInfo user, CancellationToken token = default)
        {
            var team = new Team
            {
                Id = 42,
                Name = model.Name!,
                Bio = model.Bio,
                CaptainId = user.Id,
                Captain = user
            };
            team.Members.Add(user);
            return Task.FromResult(team);
        }

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken token = default) =>
            throw new NotSupportedException();

        public void Add(object item) => throw new NotSupportedException();
        public Task SaveAsync(CancellationToken token = default) => throw new NotSupportedException();
        public Task<int> CountAsync(CancellationToken token = default) => throw new NotSupportedException();
        public Task<Team?> GetTeamById(int id, CancellationToken token = default) => throw new NotSupportedException();
        public Task<Team[]> GetUserTeams(UserInfo user, CancellationToken token = default) => throw new NotSupportedException();
        public Task<bool> CheckIsCaptain(UserInfo user, CancellationToken token = default) => throw new NotSupportedException();
        public Task<Team[]> SearchTeams(string hint, CancellationToken token = default) => throw new NotSupportedException();
        public Task<Team[]> GetTeams(int count = 100, int skip = 0, CancellationToken token = default) => throw new NotSupportedException();
        public Task<bool> AnyActiveGame(Team team, CancellationToken token = default) => throw new NotSupportedException();
        public Task Transfer(Team team, UserInfo user, CancellationToken token = default) => throw new NotSupportedException();
        public Task<bool> VerifyToken(int id, string inviteToken, CancellationToken token = default) => throw new NotSupportedException();
        public Task DeleteTeam(Team team, CancellationToken token = default) => throw new NotSupportedException();
    }

    private sealed class RecordingSocEventPublisher : ISocEventPublisher
    {
        public SocEventMessage? Published { get; private set; }

        public Guid Enqueue(SocEventMessage message) => Guid.CreateVersion7();

        public Task<Guid> PublishAsync(SocEventMessage message, CancellationToken token = default)
        {
            Published = message;
            return Task.FromResult(Guid.CreateVersion7());
        }
    }
}
