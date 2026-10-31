using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ZARRAR.Models.Internal;
using ZARRAR.Repositories.Interface;
using ZARRAR.Services.Soc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ZARRAR.Services.Variants;

internal sealed record IsolatedBuildRequest(
    Guid JobId,
    VariantTemplateKind Kind,
    string ManifestJson,
    string ManifestHash,
    string BuilderImageDigest,
    string OutputFileName,
    string SeedBase64,
    string Flag);

internal sealed record IsolatedBuildResponse(string ArtifactBase64, string GeneratorVersion);

/// <summary>
/// Dispatches deterministic builds only to an external isolated control plane. The web process never
/// executes template commands, mounts a compiler workspace, or interpolates shell input.
/// </summary>
public sealed class VariantBuilderWorker(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory clients,
    IOptions<VariantBuilderOptions> options,
    ILogger<VariantBuilderWorker> logger) : BackgroundService
{
    private readonly VariantBuilderOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var parallelism = Math.Clamp(_options.Parallelism, 1, 16);
        await Task.WhenAll(Enumerable.Range(0, parallelism).Select(_ => RunLoop(stoppingToken)));
    }

    private async Task RunLoop(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_options.Enabled || !TryGetControlPlane(out _))
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                    continue;
                }

                var processed = await ProcessOne(stoppingToken);
                if (!processed)
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Variant builder worker iteration failed.");
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }

    private async Task<bool> ProcessOne(CancellationToken token)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Atomic UPDATE + SKIP LOCKED allows several workers and web replicas without double claims.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "VariantBuildJobs"
            SET "Status" = CASE WHEN "AttemptCount" >= 3 THEN 3 ELSE 0 END,
                "CompletedAtUtc" = CASE WHEN "AttemptCount" >= 3 THEN NOW() ELSE NULL END,
                "StartedAtUtc" = NULL,
                "LastError" = 'Builder lease expired before completion.'
            WHERE "Status" = 1 AND "StartedAtUtc" < {DateTimeOffset.UtcNow.AddMinutes(-15)}
            """, token);
        var claimedId = await ClaimNextJob(db, token);
        if (claimedId is null)
            return false;

        var job = await db.VariantBuildJobs
            .Include(x => x.Template)
            .Include(x => x.Participation)
            .ThenInclude(x => x.Team)
            .SingleAsync(x => x.Id == claimedId.Value, token);

        try
        {
            var seeds = scope.ServiceProvider.GetRequiredService<DeterministicSeedService>();
            var rawSeed = seeds.Derive(job.TemplateId, job.Template.Version,
                job.Template.ChallengeId, job.ParticipationId);
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(job.SeedCommitment),
                    Convert.FromHexString(seeds.Commitment(rawSeed))))
                throw new InvalidOperationException("Stored seed commitment no longer matches derived seed.");

            var dynamicFlags = scope.ServiceProvider.GetRequiredService<DynamicFlagHmac>();
            var flag = dynamicFlags.Generate(job.Participation.GameId, job.Template.ChallengeId,
                job.Participation.TeamId, job.ParticipationId, job.FlagInstanceId);
            var request = new IsolatedBuildRequest(
                job.Id, job.Template.Kind, job.Template.ManifestJson, job.Template.ManifestHash,
                job.Template.BuilderImageDigest, job.Template.OutputFileName,
                Convert.ToBase64String(rawSeed), flag);
            var result = await Dispatch(request, token);
            byte[] artifact;
            try
            {
                artifact = Convert.FromBase64String(result.ArtifactBase64);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException("Builder returned an invalid base64 artifact.", ex);
            }
            if (artifact.Length == 0 || artifact.Length > _options.MaxArtifactBytes)
                throw new InvalidOperationException("Builder artifact violates the configured size boundary.");

            if (await db.VariantAssignments.AnyAsync(x => x.ChallengeId == job.Template.ChallengeId &&
                    x.ParticipationId == job.ParticipationId, token))
            {
                job.Status = VariantBuildStatus.Succeeded;
                job.CompletedAtUtc = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(token);
                return true;
            }

            await using var stream = new MemoryStream(artifact, writable: false);
            var blobs = scope.ServiceProvider.GetRequiredService<IBlobRepository>();
            var localFile = await blobs.CreateOrUpdateBlobFromStream(job.Template.OutputFileName, stream, token);
            var flagContext = new FlagContext
            {
                ChallengeId = job.Template.ChallengeId,
                Flag = flag,
                FlagInstanceId = job.FlagInstanceId,
                IsOccupied = true,
                Attachment = new Attachment { Type = FileType.Local, LocalFile = localFile }
            };
            db.FlagContexts.Add(flagContext);
            var assignment = new VariantAssignment
            {
                ChallengeId = job.Template.ChallengeId,
                ParticipationId = job.ParticipationId,
                FlagContext = flagContext,
                ArtifactHash = localFile.Hash,
                Mode = "Generated",
                GeneratorVersion = Limit(result.GeneratorVersion, 64),
                SeedCommitment = job.SeedCommitment,
                MetadataJson = System.Text.Json.JsonSerializer.Serialize(new
                    { jobId = job.Id, templateId = job.TemplateId, job.Template.ManifestHash })
            };
            db.VariantAssignments.Add(assignment);
            job.VariantAssignment = assignment;
            job.ArtifactHash = localFile.Hash;
            job.GeneratorVersion = assignment.GeneratorVersion;
            job.Status = VariantBuildStatus.Succeeded;
            job.CompletedAtUtc = DateTimeOffset.UtcNow;
            var events = scope.ServiceProvider.GetRequiredService<ISocEventPublisher>();
            events.Enqueue(new SocEventMessage(
                EventType: "VariantBuildSucceeded",
                Category: SocCategory.PlatformSecurity,
                Severity: SocSeverity.Info,
                Details: $"Deterministic variant build completed for challenge {job.Template.ChallengeId}.",
                Source: nameof(VariantBuilderWorker),
                TeamId: job.Participation.TeamId,
                GameId: job.Participation.GameId,
                ChallengeId: job.Template.ChallengeId,
                PayloadJson: System.Text.Json.JsonSerializer.Serialize(new
                    { job.Id, job.TemplateId, assignment.AssignmentId, assignment.ArtifactHash })));
            await db.SaveChangesAsync(token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            job.Status = job.AttemptCount >= 3 ? VariantBuildStatus.Failed : VariantBuildStatus.Pending;
            job.LastError = Limit(ex.GetBaseException().Message, 1000);
            job.CompletedAtUtc = job.Status == VariantBuildStatus.Failed ? DateTimeOffset.UtcNow : null;
            var events = scope.ServiceProvider.GetRequiredService<ISocEventPublisher>();
            events.Enqueue(new SocEventMessage(
                EventType: "VariantBuildFailed",
                Category: SocCategory.Infrastructure,
                Severity: job.Status == VariantBuildStatus.Failed ? SocSeverity.High : SocSeverity.Medium,
                Details: $"Deterministic variant build attempt failed for challenge {job.Template.ChallengeId}.",
                Source: nameof(VariantBuilderWorker),
                TeamId: job.Participation.TeamId,
                GameId: job.Participation.GameId,
                ChallengeId: job.Template.ChallengeId,
                PayloadJson: System.Text.Json.JsonSerializer.Serialize(new { job.Id, job.AttemptCount, job.Status })));
            await db.SaveChangesAsync(token);
        }
        return true;
    }

    private static async Task<Guid?> ClaimNextJob(AppDbContext db, CancellationToken token)
    {
        await db.Database.OpenConnectionAsync(token);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            UPDATE "VariantBuildJobs"
            SET "Status" = 1, "StartedAtUtc" = NOW(),
                "AttemptCount" = "AttemptCount" + 1, "LastError" = NULL
            WHERE "Id" = (
                SELECT "Id" FROM "VariantBuildJobs"
                WHERE "Status" = 0
                ORDER BY "CreatedAtUtc"
                FOR UPDATE SKIP LOCKED
                LIMIT 1
            )
            RETURNING "Id";
            """;
        var value = await command.ExecuteScalarAsync(token);
        return value is Guid id ? id : null;
    }

    private async Task<IsolatedBuildResponse> Dispatch(IsolatedBuildRequest build, CancellationToken token)
    {
        if (!TryGetControlPlane(out var origin))
            throw new InvalidOperationException("Variant builder control plane is not safely configured.");
        var client = clients.CreateClient("variant-builder");
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(origin!, "v1/builds"))
        {
            Content = JsonContent.Create(build)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiToken);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Builder returned HTTP {(int)response.StatusCode}.");
        var responseLimit = checked((long)_options.MaxArtifactBytes * 2 + 65536);
        if (response.Content.Headers.ContentLength > responseLimit)
            throw new InvalidOperationException("Builder response exceeds the configured boundary.");
        await using var source = await response.Content.ReadAsStreamAsync(token);
        await using var bounded = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(buffer, token);
            if (read == 0)
                break;
            if (bounded.Length + read > responseLimit)
                throw new InvalidOperationException("Builder response exceeds the configured boundary.");
            await bounded.WriteAsync(buffer.AsMemory(0, read), token);
        }
        return JsonSerializer.Deserialize<IsolatedBuildResponse>(bounded.GetBuffer().AsSpan(0, (int)bounded.Length),
                   new JsonSerializerOptions(JsonSerializerDefaults.Web))
               ?? throw new InvalidOperationException("Builder returned an empty response.");
    }

    private bool TryGetControlPlane(out Uri? origin)
    {
        origin = null;
        return _options.SeedKey.Length >= 32 &&
               VariantBuilderControlPlane.TryValidate(_options, out origin);
    }

    private static string Limit(string value, int length) => value.Length <= length ? value : value[..length];
}
