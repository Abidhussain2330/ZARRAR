using ZARRAR.Models.Request.Dashboard;

namespace ZARRAR.Repositories.Interface;

/// <summary>
/// Storage for the per-user interface preferences that follow a participant across devices.
/// </summary>
public interface IUserPreferencesRepository : IRepository
{
    /// <summary>
    /// Get the stored preferences of a user
    /// </summary>
    /// <param name="userId"></param>
    /// <param name="token"></param>
    /// <returns>The stored preferences, or null when the user has never saved any</returns>
    public Task<UserPreferences?> GetPreferences(Guid userId, CancellationToken token = default);

    /// <summary>
    /// Store the preferences of a user, creating the row on first write
    /// </summary>
    /// <param name="userId"></param>
    /// <param name="model">The preferences to store</param>
    /// <param name="token"></param>
    /// <returns>The stored preferences</returns>
    public Task<UserPreferences> SetPreferences(Guid userId, UserPreferencesModel model,
        CancellationToken token = default);
}
