using System.Security.Cryptography;
using System.Runtime.InteropServices;
using ZARRAR.AppHost.MinIO;
using Microsoft.Extensions.Hosting;

static void EnsureDotnetHostIsDiscoverable()
{
    var runtimeDirectory = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory());
    var dotnetRoot = runtimeDirectory.Parent?.Parent?.Parent?.FullName;
    var dotnetHost = dotnetRoot is null
        ? null
        : Path.Combine(dotnetRoot, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");

    if (dotnetRoot is null || dotnetHost is null || !File.Exists(dotnetHost))
        return;

    var currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
    var dotnetRootIsOnPath = currentPath
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Contains(dotnetRoot, StringComparer.OrdinalIgnoreCase);

    if (!dotnetRootIsOnPath)
        Environment.SetEnvironmentVariable("PATH", $"{dotnetRoot}{Path.PathSeparator}{currentPath}");

    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOTNET_ROOT")))
        Environment.SetEnvironmentVariable("DOTNET_ROOT", dotnetRoot);
}

static bool IsValidDynamicFlagHmacKey(string? encoded)
{
    if (string.IsNullOrWhiteSpace(encoded))
        return false;

    try
    {
        var key = Convert.FromBase64String(encoded);
        var valid = key.Length >= 32;
        CryptographicOperations.ZeroMemory(key);
        return valid;
    }
    catch (FormatException)
    {
        return false;
    }
}

EnsureDotnetHostIsDiscoverable();

// Values this orchestration used to hard-code, plus the obvious post-rebrand equivalent. They are
// recognised only so that they can be rejected outside Development; neither is ever used as a
// fallback. The pre-rebrand value stays on the list because it is public knowledge and existing
// deployments may still carry it.
string[] knownInsecureXorKeys = [string.Concat("gz", "ctf-xor-key"), "zarrar-xor-key"];

var builder = DistributedApplication.CreateBuilder(args);

const string postgresPasswordParameterName = "postgres-password";
var configuredPostgresPassword =
    builder.Configuration[$"Parameters:{postgresPasswordParameterName}"];

if (string.IsNullOrWhiteSpace(configuredPostgresPassword))
{
    Console.Error.WriteLine(
        "[ZARRAR] FATAL: PostgreSQL requires a stable secret. For Development, run " +
        "'dotnet user-secrets --project src/ZARRAR.AppHost set " +
        "\"Parameters:postgres-password\" \"<stable-development-password>\"'. " +
        "For production, provide Parameters:postgres-password through the deployment secret store.");
    Environment.Exit(1);
}

var postgresPassword = builder.AddParameter(postgresPasswordParameterName, secret: true);

// The XOR key protects private key material at rest, so the orchestration must never hand the
// platform a compiled-in constant. A developer can pin a value deliberately through the
// ZARRAR_XOR_KEY environment variable or an XorKey configuration entry (appsettings, user secrets,
// --XorKey=...); otherwise a fresh cryptographically random key is minted for this run.
// The ZARRAR name wins over the compatibility name when both are set.
var compatibilityXorKeyName = string.Concat("GZ", "CTF_XOR_KEY");
var xorKey = Environment.GetEnvironmentVariable("ZARRAR_XOR_KEY")
             ?? Environment.GetEnvironmentVariable(compatibilityXorKeyName)
             ?? builder.Configuration["XorKey"]
             ?? string.Empty;

if (builder.Environment.IsDevelopment())
{
    if (string.IsNullOrWhiteSpace(xorKey))
    {
        xorKey = RandomNumberGenerator.GetHexString(64, true);

        // Development only: printed so a developer can reproduce this session by pinning the value.
        Console.WriteLine($"[ZARRAR] Generated a random XorKey for this development run: {xorKey}");
        Console.WriteLine("[ZARRAR] Set ZARRAR_XOR_KEY to that value to keep it stable across runs.");
    }
}
else if (string.IsNullOrWhiteSpace(xorKey) || knownInsecureXorKeys.Contains(xorKey))
{
    Console.Error.WriteLine(
        "[ZARRAR] FATAL: XorKey is unset or still set to the known development default while running in the " +
        $"'{builder.Environment.EnvironmentName}' environment. Set the ZARRAR_XOR_KEY environment variable " +
        "to a unique, high-entropy secret before starting the AppHost.");
    Environment.Exit(1);
}

// Dynamic container flags have their own HMAC authority and must not reuse XorKey. The runtime
// DynamicFlagHmac service requires a Base64-encoded key containing at least 32 random bytes.
var dynamicFlagHmacKey = Environment.GetEnvironmentVariable("ZARRAR_DYNAMIC_FLAG_HMAC_KEY")
                         ?? builder.Configuration["DynamicFlagHmacKey"]
                         ?? string.Empty;

if (builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(dynamicFlagHmacKey))
{
    var generatedKey = RandomNumberGenerator.GetBytes(32);
    try
    {
        dynamicFlagHmacKey = Convert.ToBase64String(generatedKey);
    }
    finally
    {
        CryptographicOperations.ZeroMemory(generatedKey);
    }

    Console.WriteLine("[ZARRAR] Generated a random DynamicFlagHmacKey for this development run.");
    Console.WriteLine("[ZARRAR] Set ZARRAR_DYNAMIC_FLAG_HMAC_KEY to keep dynamic flags stable across runs.");
}

if (!IsValidDynamicFlagHmacKey(dynamicFlagHmacKey))
{
    Console.Error.WriteLine(
        "[ZARRAR] FATAL: DynamicFlagHmacKey must be Base64 encoded and contain at least 32 random bytes. " +
        "Set ZARRAR_DYNAMIC_FLAG_HMAC_KEY before starting the AppHost.");
    Environment.Exit(1);
}

var postgres = builder.AddPostgres("postgres", password: postgresPassword).WithDataVolume();

if (builder.Environment.IsDevelopment())
{
    postgres.WithPgAdmin();
}

var database = postgres.AddDatabase("database");

var redis = builder.AddRedis("redis").WithDataVolume();

// Challenge attachments and personalized artifacts must survive an AppHost restart just like the
// PostgreSQL and Redis state that references them. A named Development volume also keeps the
// supported Aspire workflow independent of a machine-specific host path.
var storage = builder.AddMinIO("minio", options =>
    options.WithDataVolume("zarrar-apphost-minio-data"));

var web = builder.AddProject<Projects.ZARRAR>("zarrar")
    .WithReference(database)
    .WaitFor(database)
    .WithReference(redis)
    .WaitFor(redis)
    .WithReference(storage)
    .WaitFor(storage)
    .WithEnvironment("Telemetry__OpenTelemetry__Enable", "true")
    .WithEnvironment("Telemetry__Prometheus__Enable", "true")
    .WithEnvironment("Development__TrustedOrigins__0", "http://localhost:63000")
    // The Development SPA opens challenge services through ZARRAR's authenticated TCP-over-
    // WebSocket proxy. Make that supported Aspire contract explicit instead of relying on a
    // developer's parent-shell environment, which disappears on a clean restart.
    .WithEnvironment("ContainerProvider__PortMappingType", "PlatformProxy")
    .WithEnvironment("ContainerProvider__EnableTrafficCapture", "true")
    .WithEnvironment("XorKey", xorKey)
    .WithEnvironment("DynamicFlagHmacKey", dynamicFlagHmacKey)
    .WithEndpoint("http", e => e.IsProxied = false)
    .WithOtlpExporter();

builder.Build().Run();
