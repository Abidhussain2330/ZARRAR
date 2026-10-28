using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using BURAQ.Models.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using ConfigModel = BURAQ.Models.Data.Config;

namespace BURAQ.Services.Config;

public class ConfigService(
    AppDbContext context,
    IDistributedCache cache,
    ILogger<ConfigService> logger,
    IOptionsSnapshot<GlobalConfig> globalConfig,
    IOptionsSnapshot<ManagedConfig> managedConfig,
    IConfiguration configuration) : IConfigService
{
    private readonly IConfigurationRoot? _configuration = configuration as IConfigurationRoot;
    private readonly byte[] _xorKey = configuration["XorKey"]?.ToUTF8Bytes() ?? [];

    public Task SaveConfig([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type type,
        object? value, CancellationToken token = default) =>
        SaveConfigSet(GetConfigs(type, value), token);

    public Task SaveConfig<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(T config,
        CancellationToken token = default) where T : class =>
        SaveConfigSet(GetConfigs(config), token);


    public byte[] GetXorKey() => _xorKey;

    public void ReloadConfig() => _configuration?.Reload();

    public async Task SaveConfigSet(HashSet<ConfigModel> configs, CancellationToken token = default)
    {
        var dbConfigs = await context.Configs
            .ToDictionaryAsync(c => c.ConfigKey, c => c, token);
        HashSet<string> cacheKeys = [];

        foreach (var conf in configs)
        {
            if (dbConfigs.TryGetValue(conf.ConfigKey, out var dbConf))
            {
                if (dbConf.Value == conf.Value)
                    continue;

                dbConf.Value = conf.Value;

                if (conf.CacheKeys is not null)
                    cacheKeys.UnionWith(conf.CacheKeys);

                // The update branch previously logged the raw value with no masking at all, so a
                // key rotation wrote the new private key straight into the log sinks (F-005).
                logger.SystemLog(
                    StaticLocalizer[nameof(Resources.Program.Config_GlobalConfigUpdated), conf.ConfigKey,
                        DescribeConfigValue(conf.ConfigKey, conf.Value)],
                    TaskStatus.Success, LogLevel.Debug);
            }
            else
            {
                if (conf.CacheKeys is not null)
                    cacheKeys.UnionWith(conf.CacheKeys);

                await context.Configs.AddAsync(conf, token);

                logger.SystemLog(
                    StaticLocalizer[nameof(Resources.Program.Config_GlobalConfigAdded), conf.ConfigKey,
                        DescribeConfigValue(conf.ConfigKey, conf.Value)],
                    TaskStatus.Success, LogLevel.Debug);
            }
        }

        await context.SaveChangesAsync(token);
        ReloadConfig();

        // flush cache
        foreach (var key in cacheKeys)
            await cache.RemoveAsync(key, token);
    }

    /// <summary>
    /// Configuration leaves whose values are safe to write to a log sink verbatim.
    /// </summary>
    /// <remarks>
    /// Redaction is deny-by-default. The previous implementation matched only the exact suffix
    /// <c>PrivateKey</c>, which left SMTP passwords, captcha secrets and every future secret-
    /// bearing setting logged in clear (finding F-005). Because configuration sections are added
    /// over time, an allow-list of demonstrably public settings is the only form of this check
    /// that stays correct as the schema grows.
    /// </remarks>
    static readonly HashSet<string> PublicConfigLeaves = new(StringComparer.Ordinal)
    {
        // GlobalConfig — site presentation
        "Title", "Slogan", "Description", "FooterInfo", "CustomTheme",
        "LogoHash", "FaviconHash", "PublicUrl", "ApiEncryption",
        // AccountPolicy
        "AllowRegister", "ActiveOnRegister", "UseCaptcha",
        "EmailConfirmationRequired", "EmailDomainList",
        // ContainerPolicy
        "AutoDestroyOnLimitReached", "MaxExerciseContainerCountPerUser",
        "DefaultLifetime", "ExtensionDuration", "RenewalWindow"
    };

    /// <summary>
    /// Render a configuration value for logging without disclosing secret material.
    /// </summary>
    /// <remarks>
    /// Non-public settings are reduced to a keyed fingerprint. That still lets an operator confirm
    /// that a value changed, and correlate the same value across entries, without the log itself
    /// becoming a credential store.
    /// </remarks>
    string DescribeConfigValue(string key, string? value)
    {
        if (value is null)
            return "null";

        var leaf = key.Split(':').LastOrDefault() ?? key;

        if (PublicConfigLeaves.Contains(leaf))
            return value;

        return $"{StaticLocalizer[nameof(Resources.Program.Config_SecretRedacted)]} " +
               FlagFingerprint.Compute(value, _xorKey);
    }

    public async Task UpdateApiTokenKeyPair(CancellationToken token = default)
    {
        var managed = managedConfig.Value;
        managed.ApiToken.RegenerateKeys(_xorKey);
        await SaveConfig(managed, token);
    }

    public async Task<SignatureContext> GetApiTokenContext(CancellationToken token = default)
    {
        var managed = managedConfig.Value;

        if (!string.IsNullOrEmpty(managed.ApiToken.PrivateKey) && !string.IsNullOrEmpty(managed.ApiToken.PublicKey))
            return new(managedConfig.Value.ApiToken, _xorKey);

        managed.ApiToken.RegenerateKeys(_xorKey);
        await SaveConfig(managed, token);
        return new(managedConfig.Value.ApiToken, _xorKey);
    }

    public async Task UpdateApiEncryptionKey(CancellationToken token = default)
    {
        var managed = managedConfig.Value;
        managed.ApiEncryption.RegenerateKeys(_xorKey);
        await SaveConfig(managed, token);
    }

    public string? DecryptApiData(string cipherText) =>
        globalConfig.Value.ApiEncryption
            ? managedConfig.Value.ApiEncryption.Decrypt(cipherText, _xorKey)
            : cipherText;

    private static void MapConfigsInternal(string key, HashSet<ConfigModel> configs, PropertyInfo info, object? value)
    {
        // ignore when value with `AutoSaveIgnoreAttribute`
        if (value is null || info.GetCustomAttribute<AutoSaveIgnoreAttribute>() != null)
            return;

        var type = info.PropertyType;
        if (type.IsArray || IsArrayLikeInterface(type))
            throw new NotSupportedException(StaticLocalizer[nameof(Resources.Program.Config_TypeNotSupported)]);

        var converter = TypeDescriptor.GetConverter(type);

        if (type == typeof(string) || type.IsValueType)
        {
            var keys = info.GetCustomAttributes<CacheFlushAttribute>()
                .Select(a => a.CacheKey).ToArray();

            configs.Add(new(key, converter.ConvertToString(value) ?? string.Empty, keys));
        }
        else if (type.IsClass)
        {
            foreach (var item in type.GetProperties())
                MapConfigsInternal($"{key}:{item.Name}", configs, item, item.GetValue(value));
        }
    }

    private static HashSet<ConfigModel> GetConfigs(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
        Type type, object? value)
    {
        HashSet<ConfigModel> configs = [];

        foreach (var item in type.GetProperties())
            MapConfigsInternal($"{type.Name}:{item.Name}", configs, item, item.GetValue(value));

        return configs;
    }

    public static HashSet<ConfigModel> GetConfigs<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
    T>(T config) where T : class
    {
        HashSet<ConfigModel> configs = [];
        var type = typeof(T);

        foreach (var item in type.GetProperties())
            MapConfigsInternal($"{type.Name}:{item.Name}", configs, item, item.GetValue(config));

        return configs;
    }

    private static bool IsArrayLikeInterface(Type type)
    {
        if (!type.IsInterface || !type.IsConstructedGenericType)
            return false;

        var genericTypeDefinition = type.GetGenericTypeDefinition();
        return genericTypeDefinition == typeof(IEnumerable<>)
               || genericTypeDefinition == typeof(ICollection<>)
               || genericTypeDefinition == typeof(IList<>)
               || genericTypeDefinition == typeof(IDictionary<,>)
               || genericTypeDefinition == typeof(ISet<>);
    }
}
