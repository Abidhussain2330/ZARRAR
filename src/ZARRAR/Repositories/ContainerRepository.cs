using ZARRAR.Models.Internal;
using ZARRAR.Models.Request.Admin;
using ZARRAR.Repositories.Interface;
using ZARRAR.Services.Cache;
using ZARRAR.Services.Container.Manager;
using ZARRAR.Services.Soc;
using ZARRAR.Services.Traffic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace ZARRAR.Repositories;

public class ContainerRepository(
    IDistributedCache cache,
    IContainerManager service,
    TrafficRecorderRegistry trafficRegistry,
    ISocEventPublisher socEvents,
    IOptionsSnapshot<ContainerProvider> provider,
    ILogger<ContainerRepository> logger,
    AppDbContext context) : RepositoryBase(context), IContainerRepository
{
    public Task<int> CountAsync(CancellationToken token = default) => Context.Containers.CountAsync(token);

    public Task<Container?> GetContainerById(Guid guid, CancellationToken token = default) =>
        Context.Containers.FirstOrDefaultAsync(i => i.Id == guid, token);

    public Task<Container?> GetContainerWithInstanceById(Guid guid, CancellationToken token = default) =>
        Context.Containers.IgnoreAutoIncludes()
            .Include(c => c.GameInstance).ThenInclude(i => i!.Challenge)
            .Include(c => c.GameInstance).ThenInclude(i => i!.FlagContext)
            .Include(c => c.GameInstance).ThenInclude(i => i!.Participation).ThenInclude(p => p.Team)
            .FirstOrDefaultAsync(i => i.Id == guid, token);

    public async Task<ContainerInstanceModel[]> GetContainerInstances(CancellationToken token = default) =>
        (await Context.Containers
            .Where(c => c.GameInstance != null)
            .Include(c => c.GameInstance).ThenInclude(i => i!.Participation)
            .OrderBy(c => c.StartedAt).ToArrayAsync(token))
        .Select(ContainerInstanceModel.FromContainer)
        .ToArray();

    public Task<Container[]> GetDyingContainers(CancellationToken token = default) =>
        Context.Containers.Where(c => c.ExpectStopAt < DateTimeOffset.UtcNow).ToArrayAsync(token);

    public Task ExtendLifetime(Container container, TimeSpan time, CancellationToken token = default)
    {
        container.ExpectStopAt += time;
        return SaveAsync(token);
    }

    public async Task<bool> ValidateContainer(Guid guid, CancellationToken token = default) =>
        await Context.Containers.AnyAsync(c => c.Id == guid, token);

    public async Task<bool> ValidateContainerAccess(Guid guid, Guid userId, CancellationToken token = default) =>
        await Context.Containers
            .IgnoreAutoIncludes()
            .AnyAsync(c => c.Id == guid &&
                           ((c.GameInstance != null &&
                             c.GameInstance.Participation.Members.Any(m => m.UserId == userId)) ||
                            (c.ExerciseInstance != null && c.ExerciseInstance.UserId == userId)),
                token);

    public async Task<bool> DestroyContainer(Container container, CancellationToken token = default)
    {
        try
        {
            var audit = await Context.Containers.AsNoTracking().IgnoreAutoIncludes()
                .Where(x => x.Id == container.Id)
                .Select(x => new
                {
                    TeamId = x.GameInstance == null ? (int?)null : x.GameInstance.Participation.TeamId,
                    UserId = x.ExerciseInstance == null ? (Guid?)null : x.ExerciseInstance.UserId,
                    GameId = x.GameInstance == null ? (int?)null : x.GameInstance.Participation.GameId,
                    ChallengeId = x.GameInstance == null
                        ? x.ExerciseInstance == null ? (int?)null : x.ExerciseInstance.ExerciseId
                        : x.GameInstance.ChallengeId,
                    IsExercise = x.ExerciseInstance != null
                })
                .SingleOrDefaultAsync(token);

            await trafficRegistry.ArchiveAsync(container.Id);

            await service.DestroyContainerAsync(container, token);

            if (container.Status != ContainerStatus.Destroyed)
                return false;

            await cache.RemoveAsync(CacheKey.ConnectionCount(container.Id), token);

            socEvents.Enqueue(new SocEventMessage(
                EventType: audit?.IsExercise == true
                    ? "ExerciseRuntimeDestroyed"
                    : "ChallengeRuntimeDestroyed",
                Category: SocCategory.Infrastructure,
                Severity: SocSeverity.Info,
                Details: $"{provider.Value.Type} destroyed a managed runtime.",
                Source: nameof(ContainerRepository),
                TeamId: audit?.TeamId,
                UserId: audit?.UserId,
                GameId: audit?.GameId,
                ChallengeId: audit?.ChallengeId,
                InstanceId: container.Id,
                PayloadJson: System.Text.Json.JsonSerializer.Serialize(new
                {
                    provider = provider.Value.Type.ToString(),
                    runtimeId = container.ContainerId
                })));

            Context.Containers.Remove(container);
            await SaveAsync(token);

            return true;
        }
        catch (Exception ex)
        {
            logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.ContainerRepository_ContainerDestroyFailed),
                    container.LogId,
                    container.Image.Split("/").LastOrDefault() ?? "", ex.Message],
                TaskStatus.Failed, LogLevel.Warning);
            return false;
        }
    }
}
