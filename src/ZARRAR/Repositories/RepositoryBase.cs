using ZARRAR.Repositories.Interface;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ZARRAR.Repositories;

public abstract class RepositoryBase(AppDbContext context) : IRepository
{
    protected readonly AppDbContext Context = context;

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken token = default) =>
        Context.Database.BeginTransactionAsync(token);

    public string ChangeTrackerView => Context.ChangeTracker.DebugView.LongView;

    /// <summary>
    /// Save changes with concurrency handling
    /// </summary>
    /// <param name="token">Cancellation token</param>
    public async Task SaveAsync(CancellationToken token = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await Context.SaveChangesAsync(token);
                return;
            }
            catch (DbUpdateConcurrencyException ex) when (attempt < 3)
            {
                foreach (var entry in ex.Entries)
                {
                    var databaseValues = await entry.GetDatabaseValuesAsync(token);
                    if (databaseValues is null)
                        throw;
                    // Preserve the requested values but retry them against the latest concurrency token.
                    entry.OriginalValues.SetValues(databaseValues);
                }
            }
        }
    }

    public void Add(object item) => Context.Add(item);

}
