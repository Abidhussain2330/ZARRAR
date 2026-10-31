using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ZARRAR.Models.Internal;
using ZARRAR.Services.Container.Manager;
using ZARRAR.Services.Container.Provider;
using ZARRAR.Utils;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ZARRAR.Test;

public sealed class MicroVmProviderTests
{
    private static MicroVmConfig ValidConfig() => new()
    {
        ControlPlaneOrigin = "https://microvm.internal.example",
        ApiToken = new string('x', 48),
        NodeName = "node-a",
        HealthPath = "v1/health"
    };

    [Fact]
    public void ConfigurationRequiresCredentialFreeHttpsAndStrongToken()
    {
        Assert.Equal("https://microvm.internal.example/",
            MicroVmConfigurationValidator.Validate(ValidConfig()).AbsoluteUri);

        var insecure = ValidConfig();
        insecure.ControlPlaneOrigin = "http://microvm.internal.example";
        Assert.Throws<InvalidOperationException>(() => MicroVmConfigurationValidator.Validate(insecure));

        var embeddedCredential = ValidConfig();
        embeddedCredential.ControlPlaneOrigin = "https://user:pass@microvm.internal.example";
        Assert.Throws<InvalidOperationException>(() => MicroVmConfigurationValidator.Validate(embeddedCredential));

        var weak = ValidConfig();
        weak.ApiToken = "short";
        Assert.Throws<InvalidOperationException>(() => MicroVmConfigurationValidator.Validate(weak));

        var whitespaceToken = ValidConfig();
        whitespaceToken.ApiToken = new string('x', 31) + " ";
        Assert.Throws<InvalidOperationException>(() => MicroVmConfigurationValidator.Validate(whitespaceToken));

        var unsafeNode = ValidConfig();
        unsafeNode.NodeName = "node-a\nforged-log";
        Assert.Throws<InvalidOperationException>(() => MicroVmConfigurationValidator.Validate(unsafeNode));
    }

    [Fact]
    public async Task ManagerSeparatesIdentityFromChallengeSecrets()
    {
        var handler = new CapturingHandler();
        var config = ValidConfig();
        var http = new HttpClient(handler) { BaseAddress = MicroVmConfigurationValidator.Validate(config) };
        var metadata = new MicroVmMetadata
        {
            Config = config,
            PortMappingType = ContainerPortMappingType.PlatformProxy,
            Capacity = 25
        };
        var provider = new TestProvider(new MicroVmClient(http, config), metadata);
        var manager = new MicroVmManager(provider, NullLogger<MicroVmManager>.Instance);

        var result = await manager.CreateContainerAsync(new ContainerConfig
        {
            Image = "registry.example/challenge@sha256:" + new string('a', 64),
            TeamId = "42",
            UserId = Guid.NewGuid(),
            GameId = 7,
            ChallengeId = 9,
            Flag = "ZARRAR{secret}",
            ExposedPort = 8080,
            CPUCount = 3,
            MemoryLimit = 256,
            StorageLimit = 512,
            NetworkMode = NetworkMode.Isolated
        });

        Assert.NotNull(result);
        Assert.True(result.IsProxy);
        using var json = JsonDocument.Parse(handler.Body!);
        var root = json.RootElement;
        Assert.Equal("42", root.GetProperty("environment").GetProperty("ZARRAR_TEAM_ID").GetString());
        Assert.False(root.GetProperty("environment").TryGetProperty("ZARRAR_FLAG", out _));
        Assert.Equal("ZARRAR{secret}",
            root.GetProperty("secretEnvironment").GetProperty("ZARRAR_FLAG").GetString());
        Assert.DoesNotContain(config.ApiToken, handler.Body!);
    }

    private sealed class TestProvider(MicroVmClient client, MicroVmMetadata metadata)
        : IContainerProvider<MicroVmClient, MicroVmMetadata>
    {
        public MicroVmClient GetProvider() => client;
        public MicroVmMetadata GetMetadata() => metadata;
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new MicroVmHealthResponse(
                        "Healthy", "node-a", 25, 0, 0, 0, "1.0", "Firecracker"))
                };

            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var json = JsonDocument.Parse(Body);
            var workloadId = json.RootElement.GetProperty("workloadId").GetGuid();
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new MicroVmCreateResponse(
                    workloadId, "fc-123", "172.31.10.2", 8080, null, null, "Running", "node-a",
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(2)))
            };
        }
    }
}
