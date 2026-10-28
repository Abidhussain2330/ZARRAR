using BURAQ.Models;
using BURAQ.Models.Data;
using BURAQ.Repositories;
using BURAQ.Utils;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BURAQ.Test;

public sealed class TestContainerPersistenceTests
{
    [Fact]
    public void AttachTestContainerTracksNewPrincipalBeforeChallengeForeignKeyIsSaved()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=tracking-only;Username=tracking;Password=tracking")
            .Options;

        using var context = new AppDbContext(options);
        var challenge = new GameChallenge
        {
            Id = 5,
            GameId = 1,
            Title = "Static container",
            Type = ChallengeType.StaticContainer
        };
        context.Attach(challenge);

        var container = new Container
        {
            Image = "local/demo:static",
            ContainerId = "provider-runtime-id",
            IP = "127.0.0.1",
            Port = 8080
        };
        var repository = new GameChallengeRepository(context, null!);

        repository.AttachTestContainer(challenge, container);
        context.ChangeTracker.DetectChanges();

        Assert.Same(container, challenge.TestContainer);
        Assert.Equal(container.Id, challenge.TestContainerId);
        Assert.Equal(EntityState.Added, context.Entry(container).State);
        Assert.Equal(EntityState.Modified, context.Entry(challenge).State);
    }
}
