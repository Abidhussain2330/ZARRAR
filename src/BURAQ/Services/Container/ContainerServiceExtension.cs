using Docker.DotNet;
using BURAQ.Models.Internal;
using BURAQ.Services.Container.Manager;
using BURAQ.Services.Container.Provider;
using k8s;

namespace BURAQ.Services.Container;

public class ContainerProviderMetadata
{
    /// <summary>
    /// The public entry address for accessing the containers
    /// </summary>
    public string PublicEntry { get; set; } = string.Empty;

    /// <summary>
    /// Port mapping type
    /// </summary>
    public ContainerPortMappingType PortMappingType { get; set; } = ContainerPortMappingType.Default;

    /// <summary>
    /// Whether to expose ports directly
    /// </summary>
    public bool ExposePort => PortMappingType == ContainerPortMappingType.Default;

    /// <summary>
    /// Mandatory sandbox baseline applied to every challenge workload
    /// </summary>
    public ContainerSandboxConfig Sandbox { get; set; } = new();

    /// <summary>Configured provider-wide workload capacity. Zero means provider-managed.</summary>
    public int Capacity { get; set; }
}

public static class ContainerServiceExtension
{
    extension(IServiceCollection services)
    {
        internal IServiceCollection AddContainerService(IConfiguration configuration)
        {
            var config = configuration.GetSection(nameof(ContainerProvider)).Get<ContainerProvider>() ??
                         new();
            if (config.MaxConcurrentInstances < 0)
                throw new InvalidOperationException(
                    "ContainerProvider:MaxConcurrentInstances must be non-negative.");

            return services.AddProvider(config).AddManager(config);
        }

        private IServiceCollection AddProvider(ContainerProvider config) =>
            config.Type switch
            {
                ContainerProviderType.Docker => services
                    .AddSingleton<IContainerProvider<DockerClient, DockerMetadata>, DockerProvider>(),
                ContainerProviderType.Kubernetes => services
                    .AddSingleton<IContainerProvider<Kubernetes, KubernetesMetadata>, KubernetesProvider>(),
                ContainerProviderType.MicroVm => services.AddHttpClient(nameof(MicroVmProvider))
                    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
                    .Services
                    .AddSingleton<IContainerProvider<MicroVmClient, MicroVmMetadata>, MicroVmProvider>(),
                _ => services
            };

        private IServiceCollection AddManager(ContainerProvider config)
            => config.Type switch
            {
                ContainerProviderType.Kubernetes => services.AddSingleton<IContainerManager, KubernetesManager>(),
                ContainerProviderType.MicroVm => services.AddSingleton<IContainerManager, MicroVmManager>(),
                _ => services.AddSingleton<IContainerManager, DockerManager>()
            };
    }
}
