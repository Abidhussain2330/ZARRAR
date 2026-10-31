using Microsoft.EntityFrameworkCore.Storage;

namespace ZARRAR.Repositories.Interface;

public interface IRepository
{
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken token = default);

    public void Add(object item);

    public Task SaveAsync(CancellationToken token = default);
}
