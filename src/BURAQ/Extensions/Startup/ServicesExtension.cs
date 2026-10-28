using System.Net.Mime;
using BURAQ.Middlewares;
using BURAQ.Models.Internal;
using BURAQ.Models.Request.Admin;
using BURAQ.Repositories;
using BURAQ.Repositories.Interface;
using BURAQ.Services;
using BURAQ.Services.Cache;
using BURAQ.Services.Config;
using BURAQ.Services.Container;
using BURAQ.Services.CronJob;
using BURAQ.Services.Mail;
using BURAQ.Services.SharedGateway;
using BURAQ.Services.Token;
using BURAQ.Services.Soc;
using BURAQ.Services.Traffic;
using BURAQ.Services.Transfer;
using BURAQ.Services.Variants;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;

namespace BURAQ.Extensions.Startup;

internal static class ServicesExtension
{
    extension(WebApplicationBuilder builder)
    {
        private void AddConfig<TConfig>()
            where TConfig : class
            => builder.Services.Configure<TConfig>(builder.Configuration.GetSection(typeof(TConfig).Name));

        internal void AddServiceConfigurations()
        {
            builder.AddConfig<EmailConfig>();
            builder.AddConfig<AccountPolicy>();
            builder.AddConfig<GlobalConfig>();
            builder.AddConfig<ManagedConfig>();
            builder.AddConfig<ContainerPolicy>();
            builder.AddConfig<ContainerProvider>();
            builder.AddConfig<SharedGatewayOptions>();
            builder.AddConfig<VariantBuilderOptions>();
            builder.Services.AddOptions<VariantBuilderOptions>()
                .Bind(builder.Configuration.GetSection(nameof(VariantBuilderOptions)))
                .Validate(options => !options.Enabled ||
                    (options.SeedKey.Length >= 32 && VariantBuilderControlPlane.TryValidate(options, out _)),
                    "Enabled VariantBuilderOptions must use a strong seed, a credential-free HTTPS root origin, a non-placeholder token, and bounded limits.")
                .ValidateOnStart();

            builder.Services.Configure<RegistrySet<RegistryConfig>>(builder.Configuration.GetSection("Registries"));

            var oldConfig = builder.Configuration.GetSection(nameof(RegistryConfig)).Get<RegistryConfig>();
            if (!string.IsNullOrWhiteSpace(oldConfig?.ServerAddress))
                // Add old config to new config set
                builder.Services.Configure<RegistrySet<RegistryConfig>>(set =>
                {
                    if (!set.TryAdd(oldConfig.ServerAddress, oldConfig))
                        set[oldConfig.ServerAddress] = oldConfig;
                });

            var forwardedOptions =
                builder.Configuration.GetSection(nameof(ForwardedOptions)).Get<ForwardedOptions>();
            if (forwardedOptions is null)
                builder.Services.Configure<ForwardedHeadersOptions>(options =>
                {
                    options.ForwardedHeaders =
                        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                });
            else
                builder.Services.Configure<ForwardedHeadersOptions>(forwardedOptions.ToForwardedHeadersOptions);
        }

        internal void AddCustomServices()
        {
            builder.Services.AddCaptchaService(builder.Configuration);
            builder.Services.AddContainerService(builder.Configuration);

            builder.Services.AddScoped<IConfigService, ConfigService>();
            builder.Services.AddScoped<ITokenService, TokenService>();
            builder.Services.AddScoped<ILogRepository, LogRepository>();
            builder.Services.AddScoped<IBlobRepository, BlobRepository>();
            builder.Services.AddScoped<IPostRepository, PostRepository>();
            builder.Services.AddScoped<IGameRepository, GameRepository>();
            builder.Services.AddScoped<ITeamRepository, TeamRepository>();
            builder.Services.AddScoped<IApiTokenRepository, ApiTokenRepository>();
            builder.Services.AddScoped<IContainerRepository, ContainerRepository>();
            builder.Services.AddScoped<IGameEventRepository, GameEventRepository>();
            builder.Services.AddScoped<ICheatInfoRepository, CheatInfoRepository>();
            builder.Services.AddScoped<IGameNoticeRepository, GameNoticeRepository>();
            builder.Services.AddScoped<ISubmissionRepository, SubmissionRepository>();
            builder.Services.AddScoped<IGameInstanceRepository, GameInstanceRepository>();
            builder.Services.AddScoped<IGameChallengeRepository, GameChallengeRepository>();
            builder.Services.AddScoped<IParticipationRepository, ParticipationRepository>();
            builder.Services.AddScoped<IExerciseInstanceRepository, ExerciseInstanceRepository>();
            builder.Services.AddScoped<IExerciseChallengeRepository, ExerciseChallengeRepository>();
            builder.Services.AddScoped<IDivisionRepository, DivisionRepository>();
            builder.Services.AddScoped<IDashboardRepository, DashboardRepository>();
            builder.Services.AddScoped<IUserPreferencesRepository, UserPreferencesRepository>();

            builder.Services.AddHttpContextAccessor();
            builder.Services.AddSingleton<SharedGatewayTokenService>();
            builder.Services.AddSingleton<SharedGatewayOriginPolicy>();
            builder.Services.AddSingleton<DeterministicSeedService>();
            builder.Services.AddSingleton<VariantManifestValidator>();
            builder.Services.AddScoped<VariantDatasetSelector>();
            builder.Services.AddHttpClient("shared-gateway", client =>
                {
                    client.Timeout = TimeSpan.FromSeconds(30);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("BURAQ-SharedGateway/1.0");
                })
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    AutomaticDecompression = System.Net.DecompressionMethods.None,
                    UseCookies = false
                })
                .AddStandardResilienceHandler();
            builder.Services.AddHttpClient("variant-builder", client =>
                {
                    client.Timeout = TimeSpan.FromMinutes(10);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("BURAQ-VariantBuilder/1.0");
                })
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    AutomaticDecompression = System.Net.DecompressionMethods.None,
                    UseCookies = false
                })
                .AddStandardResilienceHandler();
            builder.Services.AddScoped<ISocEventPublisher, SocEventPublisher>();
            builder.Services.AddScoped<ISocAlertService, SocAlertService>();
            builder.Services.AddScoped<SocDetectionQueue>();
            builder.Services.AddScoped<SocRiskCaseService>();
            // Login guessing defence + auth alerts, and the throttled sink that feeds rate-limit,
            // cross-origin and showdown anti-cheat signals into the SOC.
            builder.Services.AddScoped<AuthGuard>();
            builder.Services.AddSingleton<SecuritySignalSink>();

            builder.Services.AddScoped<ExcelHelper>();
            builder.Services.AddScoped<GameExportService>();
            builder.Services.AddScoped<GameImportService>();

            builder.Services.AddChannel<Submission>();
            builder.Services.AddChannel<CacheRequest>();
            builder.Services.AddChannel<SocDetectionEvent>();
            builder.Services.AddSingleton<CacheHelper>();
            builder.Services.AddSingleton<IMailSender, MailSender>();
            builder.Services.AddSingleton<TrafficRecorderRegistry>();
            builder.Services.AddSingleton<PublicOriginResolver>();
            builder.Services.AddSingleton<DynamicFlagHmac>();

            builder.Services.AddHostedService<CacheMaker>();
            builder.Services.AddHostedService<FlagChecker>();

            // ZARRAR 1v1 showdown: screen relay and match state (both singletons that also run
            // as hosted services — the relay's watchdog and the state loader).
            builder.Services.AddSingleton<BURAQ.Services.Zarrar.LiveRelay>();
            builder.Services.AddHostedService(sp => sp.GetRequiredService<BURAQ.Services.Zarrar.LiveRelay>());
            builder.Services.AddSingleton<BURAQ.Services.Zarrar.ZarrarDuelService>();
            builder.Services.AddHostedService(sp =>
                sp.GetRequiredService<BURAQ.Services.Zarrar.ZarrarDuelService>());
            builder.Services.AddHostedService<CronJobService>();
            builder.Services.AddSingleton<ResourceMonitoringService>();
            builder.Services.AddHostedService(sp => sp.GetRequiredService<ResourceMonitoringService>());
            builder.Services.AddHostedService<SocEventOutboxDispatcher>();
            // Keeps active cases' stored risk current as evidence ages; applies Soc:* settings.
            builder.Services.AddHostedService<SocRiskMaintenanceService>();
            // Hash-chains the SOC timeline into periodic seals (tamper evidence; S3).
            builder.Services.AddHostedService<SocLedgerService>();
            builder.Services.AddHostedService<SocEventOutboxCleanupService>();
            builder.Services.AddHostedService<SocDetectionService>();
            builder.Services.AddHostedService<VariantBuilderWorker>();
        }

        internal void AddWebServices()
        {
            builder.Services.AddRouting(options => options.LowercaseUrls = true);
            builder.Services.AddRateLimiter(RateLimiter.ConfigureRateLimiter);
            builder.Services.AddResponseCompression(options =>
            {
                options.Providers.Add<ZStandardCompressionProvider>();
                options.Providers.Add<BrotliCompressionProvider>();
                options.Providers.Add<GzipCompressionProvider>();
                options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
                    [
                        // See others in ResponseCompressionDefaults.MimeTypes
                        MediaTypeNames.Application.Pdf
                    ]
                );
                options.EnableForHttps = true;
            });

            builder.Services.AddControllersWithViews().ConfigureApiBehaviorOptions(options =>
            {
                options.InvalidModelStateResponseFactory = InvalidModelStateHandler;
            }).AddDataAnnotationsLocalization(options =>
            {
                options.DataAnnotationLocalizerProvider = (_, factory) =>
                    factory.Create(typeof(Resources.Program));
            }).AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.ConfigCustomSerializerOptions();
            });
            builder.Services.AddResponseCaching();
        }

        internal void AddDevelopmentServices()
        {
            if (!builder.Environment.IsDevelopment())
                return;

            builder.Services.AddOpenApiDocument(settings =>
            {
                settings.DocumentName = "v1";
                settings.Version = "v1";
                settings.Title = "BURAQ Server API";
                settings.Description = "BURAQ Server API Document";
                settings.UseControllerSummaryAsTagDescription = true;
                settings.SchemaSettings.TypeMappers.Add(new OpenApiDateTimeOffsetToUIntMapper());
                settings.SchemaSettings.TypeMappers.Add(new OpenApiIPAddressToStringMapper());
                settings.SchemaSettings.ReflectionService = new GenericsSystemTextJsonReflectionService();
            });
        }
    }
}
