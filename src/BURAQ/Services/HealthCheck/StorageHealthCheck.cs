using BURAQ.Storage.Interface;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BURAQ.Services.HealthCheck;

public class StorageHealthCheck(IBlobStorage blobStorage) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var fileName = $"{nameof(StorageHealthCheck)}_{Guid.CreateVersion7():N}";
            var time = DateTime.UtcNow;
            var random = Guid.NewGuid().ToString();
            await blobStorage.WriteTextAsync(fileName, random, cancellationToken: cancellationToken);

            try
            {
                if (!await blobStorage.ExistsAsync(fileName, cancellationToken) ||
                    await blobStorage.ReadTextAsync(fileName, cancellationToken: cancellationToken) != random)
                    return HealthCheckResult.Unhealthy();
            }
            finally
            {
                await blobStorage.DeleteAsync(fileName, cancellationToken);
            }

            return DateTime.UtcNow - time > TimeSpan.FromSeconds(5)
                ? HealthCheckResult.Degraded()
                : HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(exception: ex);
        }
    }
}
