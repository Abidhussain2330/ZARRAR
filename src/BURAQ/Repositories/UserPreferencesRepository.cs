using BURAQ.Models.Request.Dashboard;
using BURAQ.Repositories.Interface;
using Microsoft.EntityFrameworkCore;

namespace BURAQ.Repositories;

/// <summary>
/// Storage for the per-user interface preferences that follow a participant across devices.
/// </summary>
public class UserPreferencesRepository(
    ILogger<UserPreferencesRepository> logger,
    AppDbContext context) : RepositoryBase(context), IUserPreferencesRepository
{
    public Task<UserPreferences?> GetPreferences(Guid userId, CancellationToken token = default) =>
        Context.UserPreferences.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, token);

    public async Task<UserPreferences> SetPreferences(Guid userId, UserPreferencesModel model,
        CancellationToken token = default)
    {
        var preferences = await Context.UserPreferences.FirstOrDefaultAsync(p => p.UserId == userId, token);

        if (preferences is not null)
        {
            Apply(preferences, model);
            await Context.SaveChangesAsync(token);
            return preferences;
        }

        var inserted = new UserPreferences { UserId = userId };
        Apply(inserted, model);
        await Context.UserPreferences.AddAsync(inserted, token);

        try
        {
            await Context.SaveChangesAsync(token);
            return inserted;
        }
        catch (DbUpdateException)
        {
            // The insert may have lost a race: a second write for the same user can slip in
            // between the read above and this insert, and the unique index over "UserId" then
            // rejects ours. Discard the losing insert and look for the row that won.
            Context.Entry(inserted).State = EntityState.Detached;

            var winner = await Context.UserPreferences.FirstOrDefaultAsync(p => p.UserId == userId, token);

            // No row means this was not the insert race, so the original failure is the real one
            // and must not be hidden.
            if (winner is null)
                throw;

            logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.Preferences_ConcurrentWrite), userId],
                TaskStatus.Duplicate, LogLevel.Debug);

            // Replay the caller's update onto the surviving row, so they still observe their own
            // write. A failure here is not recoverable and propagates.
            Apply(winner, model);
            await Context.SaveChangesAsync(token);

            return winner;
        }
    }

    private static void Apply(UserPreferences preferences, UserPreferencesModel model)
    {
        preferences.Motion = model.Motion;
        preferences.LowEnergyMode = model.LowEnergyMode;
        preferences.SidebarCollapsed = model.SidebarCollapsed;
        preferences.UpdateTimeUtc = DateTimeOffset.UtcNow;
    }
}
