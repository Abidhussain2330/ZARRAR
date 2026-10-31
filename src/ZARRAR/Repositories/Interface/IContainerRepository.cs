using ZARRAR.Models.Request.Admin;

namespace ZARRAR.Repositories.Interface;

public interface IContainerRepository : IRepository
{
    Task<int> CountAsync(CancellationToken token = default);

    /// <summary>
    /// Get container by database ID
    /// </summary>
    /// <param name="guid">container ID</param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task<Container?> GetContainerById(Guid guid, CancellationToken token = default);

    /// <summary>
    /// Get container with instance info by database ID
    /// </summary>
    /// <param name="guid">container ID</param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task<Container?> GetContainerWithInstanceById(Guid guid, CancellationToken token = default);

    /// <summary>
    /// Check if the container exists by database ID
    /// </summary>
    /// <param name="guid">container ID</param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task<bool> ValidateContainer(Guid guid, CancellationToken token = default);

    /// <summary>
    /// Check whether a given user is entitled to reach the container behind the platform proxy
    /// </summary>
    /// <remarks>
    /// A container GUID is not a capability. Access is granted only to a member of the team whose
    /// participation owns the game instance, or to the owner of the exercise instance. See
    /// finding F-002.
    /// </remarks>
    /// <param name="guid">container ID</param>
    /// <param name="userId">the authenticated user ID</param>
    /// <param name="token"></param>
    /// <returns>True when the user owns the container</returns>
    public Task<bool> ValidateContainerAccess(Guid guid, Guid userId, CancellationToken token = default);

    /// <summary>
    /// Get all container instances
    /// </summary>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task<ContainerInstanceModel[]> GetContainerInstances(CancellationToken token = default);

    /// <summary>
    /// Get all containers that are about to be stopped
    /// </summary>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task<Container[]> GetDyingContainers(CancellationToken token = default);

    /// <summary>
    /// Extend container lifetime
    /// </summary>
    /// <param name="container">container</param>
    /// <param name="time">extension period</param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task ExtendLifetime(Container container, TimeSpan time, CancellationToken token = default);

    /// <summary>
    /// Destroy container and remove it from database
    /// </summary>
    /// <param name="container"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task<bool> DestroyContainer(Container container, CancellationToken token = default);
}
