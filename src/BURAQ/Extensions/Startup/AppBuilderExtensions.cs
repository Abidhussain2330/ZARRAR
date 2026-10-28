using System.Net;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Serilog;
using StackExchange.Redis;

namespace BURAQ.Extensions.Startup;

internal static class AppBuilderExtensions
{
    /// <summary>
    /// Bind the management listener that serves <c>/healthz</c> and <c>/metrics</c>.
    /// </summary>
    /// <remarks>
    /// This port was bound with <c>ListenAnyIP</c> and its endpoints are gated only on the local
    /// port number — no authentication (finding F-019). On a host where challenge containers share
    /// a network with the platform, that exposes runtime, process, database and cache metrics to
    /// participants.
    ///
    /// The default is now loopback, which keeps the shipped Docker HEALTHCHECK working because it
    /// probes <c>localhost</c> from inside the container. Deployments whose probes or scrapers
    /// connect from outside the network namespace — Kubernetes liveness probes and an external
    /// Prometheus among them — must set <c>MetricsBindAddress</c> explicitly. This is a
    /// deliberate security boundary; see INSTALLATION_AND_SETUP.md.
    /// </remarks>
    /// <summary>
    /// Count deprecated environment variables from older releases, if any.
    /// </summary>
    /// <remarks>
    /// Emitted to stderr rather than the logger because configuration is bound before Serilog
    /// has a sink. Only variable <em>names</em> are printed — several of them (XorKey, connection
    /// strings, mail credentials) carry secrets, so values are never touched.
    /// </remarks>
    static void WarnOnDeprecatedEnvironmentPrefix()
    {
        var compatibilityPrefix = string.Concat("GZ", "CTF_");
        var legacy = Environment.GetEnvironmentVariables()
            .Keys
            .Cast<object>()
            .Select(key => key.ToString())
            .Where(name => name is not null &&
                           name.StartsWith(compatibilityPrefix, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (legacy.Length == 0)
            return;

        Console.Error.WriteLine(
            $"[BURAQ] WARNING: {legacy.Length} deprecated compatibility environment variable(s) " +
            "detected. They are still honoured internally, but BURAQ_* takes precedence.");
    }

    static void BindManagementPort(KestrelServerOptions options, IConfiguration configuration)
    {
        var bind = configuration["MetricsBindAddress"];

        if (string.IsNullOrWhiteSpace(bind) ||
            bind.Equals("loopback", StringComparison.OrdinalIgnoreCase))
        {
            options.ListenLocalhost(MetricPort);
            return;
        }

        if (bind.Equals("any", StringComparison.OrdinalIgnoreCase))
        {
            options.ListenAnyIP(MetricPort);
            return;
        }

        if (IPAddress.TryParse(bind, out var address))
        {
            options.Listen(address, MetricPort);
            return;
        }

        ExitWithFatalMessage(
            StaticLocalizer[nameof(Resources.Program.Config_InvalidMetricsBindAddress), bind]);
    }

    extension(WebApplicationBuilder builder)
    {
        internal void ConfigureWebHost()
        {
            builder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.ConfigCustomSerializerOptions();
            });

            builder.Services.AddLocalization(options => options.ResourcesPath = "Resources")
                .Configure<RequestLocalizationOptions>(options =>
                {
                    options
                        .AddSupportedCultures(SupportedCultures)
                        .AddSupportedUICultures(SupportedCultures);

                    options.ApplyCurrentCultureToResponseHeaders = true;
                });

            builder.WebHost.ConfigureKestrel(options =>
            {
                var kestrelSection = builder.Configuration.GetSection("Kestrel");
                options.Configure(kestrelSection);
                kestrelSection.Bind(options);
            }).UseKestrel(options =>
            {
                options.ListenAnyIP(ServerPort);
                BindManagementPort(options, builder.Configuration);
            });

            builder.Logging.ClearProviders();
            builder.Logging.SetMinimumLevel(LogLevel.Trace);
            builder.Logging.AddSerilog(dispose: true);
            builder.Host.UseSerilog(dispose: true);
            // Compatibility variables are read first so that BURAQ_* added
            // afterwards wins on conflict. Existing deployments keep booting on their
            // old environment; new ones use BURAQ_* only. The fallback is scheduled for
            // removal — see INSTALLATION_AND_SETUP.md.
            builder.Configuration.AddEnvironmentVariables(string.Concat("GZ", "CTF_"));
            builder.Configuration.AddEnvironmentVariables("BURAQ_");
            WarnOnDeprecatedEnvironmentPrefix();

            builder.Services.AddServiceDiscovery();
            builder.Services.ConfigureHttpClientDefaults(http =>
            {
                http.AddStandardResilienceHandler();
                http.AddServiceDiscovery();
            });
        }

        internal void ConfigureCacheAndSignalR()
        {
            var signalrBuilder = builder.Services.AddSignalR().AddJsonProtocol(options =>
            {
                options.PayloadSerializerOptions.ConfigCustomSerializerOptions();
            });

            var connectionString = builder.Configuration.GetConnectionString("redis");

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                builder.Services.AddDistributedMemoryCache();
            }
            else
            {
                builder.Services.AddStackExchangeRedisCache(options =>
                {
                    options.Configuration = connectionString;
                });

                // BURAQ owns a dedicated backplane namespace.
                signalrBuilder.AddStackExchangeRedis(connectionString, options =>
                {
                    options.Configuration.ChannelPrefix = new RedisChannel("BURAQ", RedisChannel.PatternMode.Literal);
                });
            }

            builder.Services.AddMemoryCache();
        }
    }
}
