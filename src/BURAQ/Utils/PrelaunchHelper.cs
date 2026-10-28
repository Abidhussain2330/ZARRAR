using System.Security.Cryptography;
using BURAQ.Models.Internal;
using BURAQ.Services.Cache;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace BURAQ.Utils;

public static class PrelaunchHelper
{
    private const string AdminUserName = "Admin";
    private const string XorKeyConfigKey = "XorKey";
    private const string AdminPasswordConfigKey = "ADMIN_PASSWORD";

    /// <summary>
    /// Values that were once shipped as development conveniences. They are public knowledge, so they
    /// are only recognised in order to be rejected — they are never used as a fallback.
    /// </summary>
    private static readonly string[] KnownInsecureXorKeys =
        [string.Concat("gz", "ctf-xor-key"), "buraq-xor-key"];

    private static readonly string[] KnownInsecureAdminPasswords = ["Admin@2022"];

    extension(WebApplication app)
    {
        public async Task RunPrelaunchWorkAsync()
        {
            using var serviceScope = app.Services.GetRequiredService<IServiceScopeFactory>().CreateScope();

            var logger = serviceScope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            var context = serviceScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cache = serviceScope.ServiceProvider.GetRequiredService<IDistributedCache>();

            if (app.Configuration[XorKeyConfigKey] is not { Length: > 0 })
                ExitWithFatalMessage(StaticLocalizer[nameof(Resources.Program.Init_XorKeyNotSet)]);

            // Resolve eagerly so an absent, malformed, or weak dynamic-flag key stops startup
            // instead of failing only when the first competition instance is requested.
            _ = serviceScope.ServiceProvider.GetRequiredService<DynamicFlagHmac>();

            if (!app.Environment.IsDevelopment())
                app.EnsureNoInsecureDefaults();

            if (context.Database.GetMigrations().Any())
                await context.Database.MigrateAsync();

            await context.Database.EnsureCreatedAsync();

            if (!await context.Posts.AnyAsync())
            {
                await context.Posts.AddAsync(new()
                {
                    UpdateTimeUtc = DateTimeOffset.UtcNow,
                    Title = StaticLocalizer[nameof(Resources.Program.Init_PostTitle)],
                    Summary = StaticLocalizer[nameof(Resources.Program.Init_PostSummary)],
                    Content = StaticLocalizer[nameof(Resources.Program.Init_PostContent)]
                });

                await context.SaveChangesAsync();
            }

            if (app.Environment.IsDevelopment() || app.Configuration.GetSection(AdminPasswordConfigKey).Exists())
            {
                var userManager =
                    serviceScope.ServiceProvider.GetRequiredService<UserManager<UserInfo>>();

                if (await userManager.FindByNameAsync(AdminUserName) is null)
                {
                    // Development bootstraps a fresh random credential instead of a shared literal, so
                    // that no deployment can ever inherit a password that is public knowledge. Every
                    // other environment must supply one explicitly through configuration.
                    var configured = app.Configuration.GetValue<string>(AdminPasswordConfigKey);
                    var generated = app.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(configured);
                    var password = generated ? GenerateAdminPassword() : configured;

                    if (!string.IsNullOrWhiteSpace(password))
                    {
                        var admin = new UserInfo
                        {
                            UserName = AdminUserName,
                            Email = "admin@example.invalid",
                            Role = Role.Admin,
                            EmailConfirmed = true,
                            RegisterTimeUtc = DateTimeOffset.UtcNow
                        };

                        var result = await userManager.CreateAsync(admin, password);
                        if (!result.Succeeded)
                            logger.SystemLog(
                                StaticLocalizer[nameof(Resources.Program.Init_AdminCreationFailed),
                                    result.Errors.FirstOrDefault()?.Description ?? "null"], TaskStatus.Failed,
                                LogLevel.Debug);
                        else if (generated)
                        {
                            // Development only, and written straight to the console: the configured
                            // logger fans out to the database, SignalR, log files and any Loki/OTLP
                            // exporter, none of which should ever receive a live credential.
                            Console.WriteLine(StaticLocalizer[
                                nameof(Resources.Program.Init_AdminPasswordGenerated), password].Value);
                        }
                    }
                }
            }

            var containerConfig =
                serviceScope.ServiceProvider.GetRequiredService<IOptions<ContainerProvider>>();
            if (containerConfig.Value.EnableTrafficCapture &&
                containerConfig.Value.PortMappingType != ContainerPortMappingType.PlatformProxy)
                logger.SystemLog(StaticLocalizer[nameof(Resources.Program.Init_CaptureNotAvailable)],
                    TaskStatus.Failed, LogLevel.Warning);

            if (!cache.CacheCheck(logger))
                ExitWithFatalMessage(StaticLocalizer[nameof(Resources.Program.Init_InvalidCacheConfig)]);

            await cache.RemoveAsync(CacheKey.Index);
            await cache.RemoveAsync(CacheKey.ClientConfig);
            await cache.RemoveAsync(CacheKey.CaptchaConfig);
        }

        /// <summary>
        /// Refuse to start when a secret still holds a value that is public knowledge. Only called
        /// outside Development, where a known default would silently protect a real deployment.
        /// An unset XOR key is already fatal in every environment, so only the historical literals
        /// are checked here, plus an administrator password that was configured but left blank.
        /// </summary>
        private void EnsureNoInsecureDefaults()
        {
            var xorKey = app.Configuration[XorKeyConfigKey];
            if (xorKey is not null && KnownInsecureXorKeys.Contains(xorKey, StringComparer.Ordinal))
                ExitWithFatalMessage(StaticLocalizer[
                    nameof(Resources.Program.Init_XorKeyInsecure), app.Environment.EnvironmentName]);

            if (!app.Configuration.GetSection(AdminPasswordConfigKey).Exists())
                return;

            var password = app.Configuration.GetValue<string>(AdminPasswordConfigKey);
            if (string.IsNullOrWhiteSpace(password) ||
                KnownInsecureAdminPasswords.Contains(password, StringComparer.Ordinal))
                ExitWithFatalMessage(StaticLocalizer[
                    nameof(Resources.Program.Init_AdminPasswordInsecure), app.Environment.EnvironmentName]);
        }
    }

    extension(IDistributedCache cache)
    {
        private bool CacheCheck(ILogger<Program> logger)
        {
            var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
            var cacheVersion = $"BURAQ@{version}";

            try
            {
                cache.SetString("_ValidCheck", cacheVersion);
                return cache.GetString("_ValidCheck") == cacheVersion;
            }
            catch (Exception e)
            {
                logger.LogErrorMessage(e, StaticLocalizer[nameof(Resources.Program.Init_InvalidCacheConfig)]);
                return false;
            }
        }
    }

    /// <summary>
    /// Build a random administrator password. One character of every class required by the identity
    /// password policy is seeded first, the remainder is drawn from the full alphabet, and the whole
    /// buffer is shuffled so that the class positions are not predictable.
    /// </summary>
    private static string GenerateAdminPassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";
        const string symbols = "!@#$%^&*-_=+";
        const string alphabet = upper + lower + digits + symbols;

        Span<char> password = stackalloc char[24];

        password[0] = upper[RandomNumberGenerator.GetInt32(upper.Length)];
        password[1] = lower[RandomNumberGenerator.GetInt32(lower.Length)];
        password[2] = digits[RandomNumberGenerator.GetInt32(digits.Length)];
        password[3] = symbols[RandomNumberGenerator.GetInt32(symbols.Length)];

        for (var index = 4; index < password.Length; index++)
            password[index] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];

        RandomNumberGenerator.Shuffle(password);

        return new string(password);
    }
}
