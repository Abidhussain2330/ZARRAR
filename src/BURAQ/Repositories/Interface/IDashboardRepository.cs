using BURAQ.Models;
using BURAQ.Models.Request.Dashboard;

namespace BURAQ.Repositories.Interface;

/// <summary>
/// Aggregate queries backing the participant command centre dashboard.
/// </summary>
/// <remarks>
/// Every method takes an explicit <c>includeHidden</c> flag instead of reading the caller's role
/// itself. The controller resolves the flag once from the authenticated principal and passes it
/// down, so the visibility decision is made in exactly one place and is visible at every call
/// site. When the flag is false, hidden games and disabled challenges are excluded from the query
/// itself rather than filtered out afterwards.
/// </remarks>
public interface IDashboardRepository : IRepository
{
    /// <summary>
    /// Get the platform-wide counters, with caching
    /// </summary>
    /// <param name="includeHidden">Whether hidden games may be counted</param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task<DashboardOverviewModel> GetOverview(bool includeHidden, CancellationToken token = default);

    /// <summary>
    /// Get the most recent accepted submissions across visible games, with caching
    /// </summary>
    /// <param name="includeHidden">Whether solves from hidden games may be returned</param>
    /// <param name="count">
    /// Requested number of entries; clamped to <see cref="Limits.MaxDashboardLiveSolveCount" />
    /// </param>
    /// <param name="token"></param>
    /// <returns></returns>
    public Task<LiveSolveModel[]> GetLiveSolves(bool includeHidden, int count, CancellationToken token = default);

    /// <summary>
    /// Get the ID of the most relevant game for a user: an active game they have joined, otherwise
    /// the soonest upcoming game
    /// </summary>
    /// <param name="userId">The calling user</param>
    /// <param name="includeHidden">Whether hidden games may be selected</param>
    /// <param name="token"></param>
    /// <returns>The game ID, or null when no game qualifies</returns>
    public Task<int?> GetFeaturedGameId(Guid userId, bool includeHidden, CancellationToken token = default);

    /// <summary>
    /// Get a game by ID, but only if the caller is allowed to see it
    /// </summary>
    /// <param name="gameId"></param>
    /// <param name="includeHidden">Whether a hidden game may be returned</param>
    /// <param name="token"></param>
    /// <returns>The game, or null when it does not exist or is hidden from the caller</returns>
    public Task<Game?> GetVisibleGame(int gameId, bool includeHidden, CancellationToken token = default);
}
