using System.Text.Json;
using BURAQ.Models.Internal;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BURAQ.Test;

public sealed class ClientConfigRegistrationPolicyTests
{
    [Fact]
    public void PublicClientConfigurationPublishesDisabledRegistrationState()
    {
        var services = new ServiceCollection();
        services.AddOptions<GlobalConfig>();
        services.AddOptions<ContainerPolicy>();
        services.AddOptions<ContainerProvider>();
        services.AddOptions<ManagedConfig>();
        services.AddOptions<AccountPolicy>().Configure(policy => policy.AllowRegister = false);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var configuration = ClientConfig.FromServiceProvider(scope.ServiceProvider);

        Assert.Contains("\"allowRegister\":false",
            JsonSerializer.Serialize(configuration, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }
}
