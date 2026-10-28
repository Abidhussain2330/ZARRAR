using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace BURAQ.Services.Soc;

/// <summary>
/// Password-guessing defence and detection for the login endpoint.
/// </summary>
/// <remarks>
/// <para>
/// ASP.NET Identity's lockout is per account: ten wrong passwords from anyone lock the real owner
/// out. In a head-to-head event the opponent's user name is on the projector, so that lockout is
/// itself an attack (lock the rival out minutes before the match). This guard throttles per
/// (account, source address) instead, with a much higher per-account ceiling for distributed
/// guessing, so a guesser only ever locks out themselves.
/// </para>
/// <para>
/// It also turns failures into SOC alerts: one address failing against many accounts (password
/// spraying) and many failures against one account (brute force). Counters live in the
/// distributed cache (Redis in production), so they survive across nodes and expire on their own.
/// </para>
/// </remarks>
public sealed class AuthGuard(IDistributedCache cache, ISocAlertService alerts, ILogger<AuthGuard> logger)
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    /// <summary>Failures from one address against one account before that pair is blocked.</summary>
    public const int PairLimit = 8;

    /// <summary>Failures against one account from all addresses before it is soft-locked.</summary>
    public const int AccountLimit = 60;

    /// <summary>Distinct accounts failed from one address that count as password spraying.</summary>
    private const int SprayAccounts = 5;

    /// <summary>Failures against one account (any address) that raise a brute-force alert.</summary>
    private const int BruteForceAlert = 20;

    private static readonly DistributedCacheEntryOptions Expiry = new() { AbsoluteExpirationRelativeToNow = Window };

    /// <summary>Whether this address may try a password for this account right now.</summary>
    public async Task<bool> IsBlockedAsync(Guid userId, string address, CancellationToken token)
    {
        try
        {
            return await ReadCount(PairKey(userId, address), token) >= PairLimit ||
                   await ReadCount(AccountKey(userId), token) >= AccountLimit;
        }
        catch (Exception e)
        {
            // Cache down: fail open for availability; the per-IP rate limit still applies.
            logger.LogWarning(e, "Login guard unavailable; allowing the attempt.");
            return false;
        }
    }

    /// <summary>Records a failed attempt (userId null: the account does not exist).</summary>
    public async Task RecordFailureAsync(Guid? userId, string attemptedName, string address, CancellationToken token)
    {
        try
        {
            if (userId is { } id)
            {
                await Increment(PairKey(id, address), token);
                var accountFailures = await Increment(AccountKey(id), token);

                if (accountFailures == BruteForceAlert || accountFailures == AccountLimit)
                    await alerts.CreateAsync(new SocAlert
                    {
                        SourceKey = $"auth-brute:{id:N}:{Bucket()}:{accountFailures}",
                        Event = accountFailures >= AccountLimit ? "Account Soft-Locked" : "Password Brute Force",
                        Description = accountFailures >= AccountLimit
                            ? $"{accountFailures} failed logins against one account in 15 minutes; new attempts are refused until the window passes."
                            : $"{accountFailures} failed logins against one account in 15 minutes.",
                        Severity = accountFailures >= AccountLimit ? SocSeverity.Critical : SocSeverity.High,
                        Category = SocCategory.Authentication,
                        RiskImpact = accountFailures >= AccountLimit ? 60 : 35,
                        Evidence = JsonSerializer.Serialize(new { userId = id, lastAddress = address, accountFailures })
                    }, token);
            }

            // Spraying: distinct account names failed from one address.
            var names = await ReadSet(SprayKey(address), token);
            var nameHash = Hash(attemptedName.Trim().ToLowerInvariant());
            if (names.Add(nameHash) && names.Count <= 50)
            {
                await cache.SetStringAsync(SprayKey(address), JsonSerializer.Serialize(names), Expiry, token);

                if (names.Count == SprayAccounts)
                    await alerts.CreateAsync(new SocAlert
                    {
                        SourceKey = $"auth-spray:{Hash(address)[..24]}:{Bucket()}",
                        Event = "Password Spraying",
                        Description = $"One network address failed logins against {names.Count} different accounts in 15 minutes.",
                        Severity = SocSeverity.High,
                        Category = SocCategory.Authentication,
                        RiskImpact = 40,
                        Evidence = JsonSerializer.Serialize(new { address, distinctAccounts = names.Count })
                    }, token);
            }
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not record a failed login.");
        }
    }

    /// <summary>Accounts registered from one address within an hour that suggest sock puppets.</summary>
    private const int RegistrationBurst = 4;

    /// <summary>
    /// S2 — registration bursts: one address creating several accounts within an hour (one
    /// person preparing extra accounts, or a script). One alert per address per hour.
    /// </summary>
    public async Task RecordRegistrationAsync(Guid userId, string address, CancellationToken token)
    {
        try
        {
            var key = $"_Reg_Addr:{Hash(address)}";
            var json = await cache.GetStringAsync(key, token);
            List<Guid> accounts = string.IsNullOrEmpty(json)
                ? new List<Guid>()
                : JsonSerializer.Deserialize<List<Guid>>(json) ?? new List<Guid>();
            if (accounts.Contains(userId) || accounts.Count >= 50)
                return;

            accounts.Add(userId);
            await cache.SetStringAsync(key, JsonSerializer.Serialize(accounts),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1) }, token);

            if (accounts.Count == RegistrationBurst)
                await alerts.CreateAsync(new SocAlert
                {
                    SourceKey = $"reg-burst:{Hash(address)[..24]}:{DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 3600}",
                    Event = "Registration Burst",
                    Description = $"{accounts.Count} accounts were registered from one network address within an hour (possible sock-puppet accounts).",
                    Severity = SocSeverity.Medium,
                    Category = SocCategory.Authentication,
                    RiskImpact = 25,
                    Evidence = JsonSerializer.Serialize(new { address, accounts })
                }, token);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not record a registration.");
        }
    }

    /// <summary>A successful login clears that address's counter for the account.</summary>
    public async Task RecordSuccessAsync(Guid userId, string address, CancellationToken token)
    {
        try
        {
            await cache.RemoveAsync(PairKey(userId, address), token);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not reset the login counter.");
        }
    }

    private async Task<int> ReadCount(string key, CancellationToken token) =>
        int.TryParse(await cache.GetStringAsync(key, token), out var n) ? n : 0;

    // Not atomic: two racing failures may count as one. Thresholds tolerate that.
    private async Task<int> Increment(string key, CancellationToken token)
    {
        var next = await ReadCount(key, token) + 1;
        await cache.SetStringAsync(key, next.ToString(), Expiry, token);
        return next;
    }

    private async Task<HashSet<string>> ReadSet(string key, CancellationToken token)
    {
        var json = await cache.GetStringAsync(key, token);
        if (string.IsNullOrEmpty(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<HashSet<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string PairKey(Guid userId, string address) => $"_AuthFail_Pair:{userId:N}:{Hash(address)}";
    private static string AccountKey(Guid userId) => $"_AuthFail_Acct:{userId:N}";
    private static string SprayKey(string address) => $"_AuthFail_Spray:{Hash(address)}";

    /// <summary>The current 15-minute window, so each burst raises one alert, not one ever.</summary>
    private static long Bucket() => DateTimeOffset.UtcNow.ToUnixTimeSeconds() / (long)Window.TotalSeconds;

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
