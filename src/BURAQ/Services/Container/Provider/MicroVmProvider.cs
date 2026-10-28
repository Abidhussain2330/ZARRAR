using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BURAQ.Models.Internal;
using Microsoft.Extensions.Options;

namespace BURAQ.Services.Container.Provider;

public sealed class MicroVmMetadata : ContainerProviderMetadata
{
    public MicroVmConfig Config { get; init; } = new();
}

/// <summary>Bounded HTTPS client for a trusted Firecracker/Cloud Hypervisor control-plane agent.</summary>
public sealed class MicroVmClient(HttpClient http, MicroVmConfig config)
{
    private const int MaximumResponseBytes = 64 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<MicroVmCreateResponse> CreateAsync(MicroVmCreateRequest request,
        CancellationToken token)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "v1/microvms")
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        message.Headers.Add("Idempotency-Key", request.WorkloadId.ToString("N"));
        return await SendAsync<MicroVmCreateResponse>(message, token);
    }

    public async Task DeleteAsync(string runtimeId, Guid workloadId, CancellationToken token)
    {
        using var message = new HttpRequestMessage(HttpMethod.Delete,
            $"v1/microvms/{Uri.EscapeDataString(runtimeId)}");
        message.Headers.Add("Idempotency-Key", $"delete-{workloadId:N}");
        using var timeout = RequestTimeout(token);
        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return;
        response.EnsureSuccessStatusCode();
    }

    public async Task<MicroVmHealthResponse> HealthAsync(CancellationToken token)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, config.HealthPath.TrimStart('/'));
        return await SendAsync<MicroVmHealthResponse>(message, token);
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage message, CancellationToken token)
    {
        using var timeout = RequestTimeout(token);
        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        var payload = await ReadBoundedAsync(response.Content, timeout.Token);
        return JsonSerializer.Deserialize<T>(payload, JsonOptions) ??
               throw new InvalidOperationException("The MicroVM control plane returned an empty response.");
    }

    private CancellationTokenSource RequestTimeout(CancellationToken token)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(config.RequestTimeoutSeconds, 5, 120)));
        return timeout;
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken token)
    {
        if (content.Headers.ContentLength > MaximumResponseBytes)
            throw new InvalidOperationException("The MicroVM control-plane response exceeded 64 KiB.");
        await using var input = await content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await input.ReadAsync(buffer, token);
            if (read == 0) break;
            if (output.Length + read > MaximumResponseBytes)
                throw new InvalidOperationException("The MicroVM control-plane response exceeded 64 KiB.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}

public sealed class MicroVmProvider : IContainerProvider<MicroVmClient, MicroVmMetadata>
{
    private readonly MicroVmClient _client;
    private readonly MicroVmMetadata _metadata;

    public MicroVmProvider(IOptions<ContainerProvider> options, IHttpClientFactory clients,
        ILogger<MicroVmProvider> logger)
    {
        var config = options.Value.MicroVmConfig ?? new();
        var origin = MicroVmConfigurationValidator.Validate(config);
        var http = clients.CreateClient(nameof(MicroVmProvider));
        http.BaseAddress = origin;
        http.Timeout = Timeout.InfiniteTimeSpan;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiToken);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("BURAQ-MicroVM-ControlPlane/1.0");

        _client = new MicroVmClient(http, config);
        _metadata = new MicroVmMetadata
        {
            Config = config,
            PortMappingType = options.Value.PortMappingType,
            PublicEntry = options.Value.PublicEntry,
            Sandbox = options.Value.Sandbox,
            Capacity = options.Value.MaxConcurrentInstances
        };
        logger.LogInformation("MicroVM provider configured for node {Node} over HTTPS.", config.NodeName);
    }

    public MicroVmClient GetProvider() => _client;
    public MicroVmMetadata GetMetadata() => _metadata;
}

public static class MicroVmConfigurationValidator
{
    public static Uri Validate(MicroVmConfig config)
    {
        if (!Uri.TryCreate(config.ControlPlaneOrigin, UriKind.Absolute, out var origin) ||
            origin.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(origin.UserInfo) ||
            !string.IsNullOrEmpty(origin.Query) || !string.IsNullOrEmpty(origin.Fragment) ||
            origin.AbsolutePath != "/" || string.IsNullOrWhiteSpace(origin.Host))
            throw new InvalidOperationException("MicroVmConfig.ControlPlaneOrigin must be a credential-free HTTPS origin.");
        if (string.IsNullOrWhiteSpace(config.ApiToken) || config.ApiToken.Length < 32 ||
            config.ApiToken.Any(char.IsWhiteSpace))
            throw new InvalidOperationException("MicroVmConfig.ApiToken must contain at least 32 characters.");
        if (string.IsNullOrWhiteSpace(config.NodeName) || config.NodeName.Length > 80 ||
            config.NodeName.Any(x => !(char.IsAsciiLetterOrDigit(x) || x is '-' or '_' or '.' or ':')))
            throw new InvalidOperationException("MicroVmConfig.NodeName must contain 1-80 characters.");
        var health = config.HealthPath.TrimStart('/');
        if (string.IsNullOrWhiteSpace(health) || health.Contains("..", StringComparison.Ordinal) ||
            health.Any(x => !(char.IsAsciiLetterOrDigit(x) || x is '/' or '-' or '_' or '.')))
            throw new InvalidOperationException("MicroVmConfig.HealthPath must be a safe relative path.");

        var normalized = origin.AbsoluteUri.EndsWith('/') ? origin : new Uri(origin.AbsoluteUri + '/');
        return normalized;
    }
}

public sealed record MicroVmCreateRequest(
    Guid WorkloadId, string OciImage, int ExposedPort, int CpuTenths, int MemoryMb, int StorageMb,
    string NetworkMode, bool EnableTrafficCapture, IReadOnlyDictionary<string, string> Environment,
    IReadOnlyDictionary<string, string> SecretEnvironment);

public sealed record MicroVmCreateResponse(
    Guid WorkloadId, string RuntimeId, string PrivateAddress, int PrivatePort,
    string? PublicAddress, int? PublicPort, string State, string Node,
    DateTimeOffset StartedAtUtc, DateTimeOffset ExpiresAtUtc);

public sealed record MicroVmHealthResponse(
    string Status, string Node, int Capacity, int Running, int Pending, int Unavailable,
    string? Version, string? Isolation);
