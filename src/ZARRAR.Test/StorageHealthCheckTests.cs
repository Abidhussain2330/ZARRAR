using System.Collections.Concurrent;
using ZARRAR.Services.HealthCheck;
using ZARRAR.Storage.Interface;
using ZARRAR.Utils;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace ZARRAR.Test;

public class StorageHealthCheckTests
{
    [Fact]
    public async Task Concurrent_checks_use_isolated_probe_objects()
    {
        var storage = new CoordinatedInMemoryBlobStorage(expectedWrites: 2);
        var first = new StorageHealthCheck(storage);
        var second = new StorageHealthCheck(storage);
        var context = new HealthCheckContext();

        var results = await Task.WhenAll(
            first.CheckHealthAsync(context),
            second.CheckHealthAsync(context));

        Assert.All(results, result => Assert.Equal(HealthStatus.Healthy, result.Status));
        Assert.Equal(2, storage.WrittenPaths.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(storage.WrittenPaths, storage.Exists);
    }

    private sealed class CoordinatedInMemoryBlobStorage(int expectedWrites) : IBlobStorage
    {
        private readonly ConcurrentDictionary<string, string> _objects = new(StringComparer.Ordinal);
        private readonly TaskCompletionSource _writesReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _writes;

        public ConcurrentQueue<string> WrittenPaths { get; } = [];

        public Task EnsureInitializedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(_objects.ContainsKey(path));
        public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
        {
            _objects.TryRemove(path, out _);
            return Task.CompletedTask;
        }
        public bool Exists(string path) => _objects.ContainsKey(path);
        public Task<StorageItem> GetBlobAsync(string path, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<int> CountAsync(string path, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task WriteAsync(string path, Stream content, bool append = false,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task WriteFileAsync(string path, string localFilePath, CompressionFormat format = CompressionFormat.None,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async Task WriteTextAsync(string path, string contents, CancellationToken cancellationToken = default)
        {
            _objects[path] = contents;
            WrittenPaths.Enqueue(path);
            if (Interlocked.Increment(ref _writes) == expectedWrites)
                _writesReady.TrySetResult();
            await _writesReady.Task.WaitAsync(cancellationToken);
        }
        public Task<string> ReadTextAsync(string path, CancellationToken cancellationToken = default) =>
            _objects.TryGetValue(path, out var value)
                ? Task.FromResult(value)
                : throw new FileNotFoundException();
        public Task<IReadOnlyList<StorageItem>> ListAsync(string path, bool useFlatListing = false,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
