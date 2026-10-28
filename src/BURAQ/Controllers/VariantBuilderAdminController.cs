using System.Net.Mime;
using BURAQ.Middlewares;
using BURAQ.Models.Internal;
using BURAQ.Models.Request.Admin;
using BURAQ.Services.Soc;
using BURAQ.Services.Variants;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BURAQ.Controllers;

[RequireAdmin]
[ApiController]
[Route("api/admin/variant-builder")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class VariantBuilderAdminController(
    AppDbContext db,
    VariantManifestValidator validator,
    DeterministicSeedService seeds,
    IOptions<VariantBuilderOptions> options,
    ISocEventPublisher socEvents) : ControllerBase
{
    [HttpGet("status")]
    public async Task<ActionResult<VariantBuilderStatusModel>> Status(CancellationToken token)
    {
        var jobs = db.VariantBuildJobs.AsNoTracking();
        var configured = seeds.IsConfigured && VariantBuilderControlPlane.TryValidate(options.Value, out _);
        return new VariantBuilderStatusModel(
            options.Value.Enabled, configured,
            await db.VariantTemplates.CountAsync(x => x.Status == VariantTemplateStatus.Validated, token),
            await db.VariantDatasetItems.CountAsync(x => x.IsEnabled && x.SelectedAtUtc == null, token),
            await jobs.CountAsync(x => x.Status == VariantBuildStatus.Pending, token),
            await jobs.CountAsync(x => x.Status == VariantBuildStatus.Running, token),
            await jobs.CountAsync(x => x.Status == VariantBuildStatus.Succeeded, token),
            await jobs.CountAsync(x => x.Status == VariantBuildStatus.Failed, token),
            await jobs.Where(x => x.Status == VariantBuildStatus.Pending)
                .MinAsync(x => (DateTimeOffset?)x.CreatedAtUtc, token));
    }

    [HttpGet("templates")]
    public async Task<ActionResult<VariantTemplateModel[]>> Templates(CancellationToken token) =>
        await db.VariantTemplates.AsNoTracking().OrderByDescending(x => x.UpdatedAtUtc)
            .Select(x => new VariantTemplateModel(
                x.Id, x.ChallengeId, x.Challenge.Title, x.Version, x.Kind, x.ManifestHash,
                x.BuilderImageDigest, x.OutputFileName, x.Status, x.Jobs.Count,
                x.Jobs.Count(j => j.Status == VariantBuildStatus.Succeeded),
                x.Jobs.Count(j => j.Status == VariantBuildStatus.Failed),
                x.DatasetItems.Count, x.UpdatedAtUtc, x.ValidatedAtUtc))
            .ToArrayAsync(token);

    [HttpPut("challenges/{challengeId:int}/templates")]
    public async Task<ActionResult<VariantTemplateModel>> CreateTemplate(int challengeId,
        VariantTemplateCreateModel model, CancellationToken token)
    {
        var challenge = await db.GameChallenges.SingleOrDefaultAsync(x => x.Id == challengeId, token);
        if (challenge is null)
            return NotFound();
        if (challenge.Type != ChallengeType.DynamicAttachment)
            return Conflict(new RequestResponse("Variant templates require a dynamic-attachment challenge.",
                StatusCodes.Status409Conflict));
        if (await db.VariantTemplates.AnyAsync(x => x.ChallengeId == challengeId && x.Version == model.Version, token))
            return Conflict(new RequestResponse("Template version already exists.", StatusCodes.Status409Conflict));

        var validation = validator.Validate(model.ManifestJson, model.BuilderImageDigest, model.OutputFileName);
        var template = new VariantTemplate
        {
            ChallengeId = challengeId,
            Version = model.Version,
            Kind = model.Kind,
            ManifestJson = model.ManifestJson,
            ManifestHash = validation.ManifestHash,
            BuilderImageDigest = model.BuilderImageDigest,
            OutputFileName = model.OutputFileName,
            Status = validation.IsValid ? VariantTemplateStatus.Validated : VariantTemplateStatus.Draft,
            ValidatedAtUtc = validation.IsValid ? DateTimeOffset.UtcNow : null
        };
        db.VariantTemplates.Add(template);
        socEvents.Enqueue(new SocEventMessage(
            EventType: "VariantTemplateCreated", Category: SocCategory.PlatformSecurity,
            Severity: validation.IsValid ? SocSeverity.Info : SocSeverity.Medium,
            Details: $"Variant template {model.Version} created for challenge {challengeId}.",
            Source: nameof(VariantBuilderAdminController), GameId: challenge.GameId, ChallengeId: challengeId,
            PayloadJson: System.Text.Json.JsonSerializer.Serialize(new
                { template.Version, template.Kind, template.ManifestHash, template.Status, validation.Errors })));
        await db.SaveChangesAsync(token);
        return Ok(new VariantTemplateModel(template.Id, challengeId, challenge.Title, template.Version,
            template.Kind, template.ManifestHash, template.BuilderImageDigest, template.OutputFileName,
            template.Status, 0, 0, 0, 0, template.UpdatedAtUtc, template.ValidatedAtUtc));
    }

    [HttpPost("templates/{templateId:long}/validate")]
    public async Task<ActionResult<VariantManifestValidation>> Validate(long templateId, CancellationToken token)
    {
        var template = await db.VariantTemplates.Include(x => x.Challenge)
            .SingleOrDefaultAsync(x => x.Id == templateId, token);
        if (template is null)
            return NotFound();
        var result = validator.Validate(template.ManifestJson, template.BuilderImageDigest, template.OutputFileName);
        template.ManifestHash = result.ManifestHash;
        template.Status = result.IsValid ? VariantTemplateStatus.Validated : VariantTemplateStatus.Draft;
        template.ValidatedAtUtc = result.IsValid ? DateTimeOffset.UtcNow : null;
        template.UpdatedAtUtc = DateTimeOffset.UtcNow;
        socEvents.Enqueue(new SocEventMessage(
            EventType: "VariantTemplateValidated", Category: SocCategory.PlatformSecurity,
            Severity: result.IsValid ? SocSeverity.Info : SocSeverity.Medium,
            Details: $"Variant template validation {(result.IsValid ? "passed" : "failed")} for challenge {template.ChallengeId}.",
            Source: nameof(VariantBuilderAdminController), GameId: template.Challenge.GameId,
            ChallengeId: template.ChallengeId,
            PayloadJson: System.Text.Json.JsonSerializer.Serialize(new
                { template.Id, template.ManifestHash, result.IsValid, result.Errors })));
        await db.SaveChangesAsync(token);
        return Ok(result);
    }

    [HttpPost("templates/{templateId:long}/jobs")]
    public async Task<ActionResult<RequestResponse<int>>> Schedule(long templateId, CancellationToken token)
    {
        if (!seeds.IsConfigured)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new RequestResponse("Variant seed service is not configured.", StatusCodes.Status503ServiceUnavailable));
        var template = await db.VariantTemplates.Include(x => x.Challenge)
            .SingleOrDefaultAsync(x => x.Id == templateId, token);
        if (template is null)
            return NotFound();
        if (template.Status != VariantTemplateStatus.Validated)
            return Conflict(new RequestResponse("Template must pass validation before jobs are scheduled.",
                StatusCodes.Status409Conflict));

        var existing = db.VariantBuildJobs
            .Where(x => x.Template.ChallengeId == template.ChallengeId)
            .Select(x => x.ParticipationId);
        var assigned = db.VariantAssignments.Where(x => x.ChallengeId == template.ChallengeId)
            .Select(x => x.ParticipationId);
        var participants = await db.Participations.AsNoTracking()
            .Where(x => x.GameId == template.Challenge.GameId && x.Status == ParticipationStatus.Accepted &&
                !existing.Contains(x.Id) && !assigned.Contains(x.Id))
            .Select(x => x.Id)
            .ToArrayAsync(token);
        var jobs = participants.Select(participationId =>
        {
            var rawSeed = seeds.Derive(template.Id, template.Version, template.ChallengeId, participationId);
            return new VariantBuildJob
            {
                TemplateId = template.Id,
                ParticipationId = participationId,
                SeedCommitment = seeds.Commitment(rawSeed)
            };
        }).ToArray();
        db.VariantBuildJobs.AddRange(jobs);
        socEvents.Enqueue(new SocEventMessage(
            EventType: "VariantBuildJobsScheduled", Category: SocCategory.PlatformSecurity,
            Severity: SocSeverity.Info,
            Details: $"{jobs.Length} deterministic variant build jobs were scheduled.",
            Source: nameof(VariantBuilderAdminController), GameId: template.Challenge.GameId,
            ChallengeId: template.ChallengeId,
            PayloadJson: System.Text.Json.JsonSerializer.Serialize(new { template.Id, count = jobs.Length })));
        await db.SaveChangesAsync(token);
        return Ok(new RequestResponse<int>("Jobs scheduled.", jobs.Length, StatusCodes.Status200OK));
    }

    [HttpGet("jobs")]
    public async Task<ActionResult<VariantBuildJobModel[]>> Jobs([FromQuery] long? templateId,
        [FromQuery] VariantBuildStatus? status, CancellationToken token)
    {
        var query = db.VariantBuildJobs.AsNoTracking().AsQueryable();
        if (templateId is not null) query = query.Where(x => x.TemplateId == templateId);
        if (status is not null) query = query.Where(x => x.Status == status);
        return await query.OrderByDescending(x => x.CreatedAtUtc).Take(500)
            .Select(x => new VariantBuildJobModel(x.Id, x.TemplateId, x.ParticipationId,
                x.Participation.TeamId, x.Participation.Team.Name, x.Status, x.AttemptCount,
                x.SeedCommitment, x.ArtifactHash, x.GeneratorVersion, x.LastError,
                x.CreatedAtUtc, x.CompletedAtUtc))
            .ToArrayAsync(token);
    }

    [HttpPost("jobs/{jobId:guid}/retry")]
    public async Task<IActionResult> Retry(Guid jobId, CancellationToken token)
    {
        var job = await db.VariantBuildJobs.Include(x => x.Template).ThenInclude(x => x.Challenge)
            .SingleOrDefaultAsync(x => x.Id == jobId, token);
        if (job is null)
            return NotFound();
        if (job.Status is not (VariantBuildStatus.Failed or VariantBuildStatus.Quarantined) ||
            job.VariantAssignmentId is not null)
            return Conflict(new RequestResponse("Only unassigned failed or quarantined jobs can be retried.",
                StatusCodes.Status409Conflict));
        job.Status = VariantBuildStatus.Pending;
        job.AttemptCount = 0;
        job.StartedAtUtc = null;
        job.CompletedAtUtc = null;
        job.LastError = null;
        socEvents.Enqueue(new SocEventMessage(
            EventType: "VariantBuildRetried", Category: SocCategory.Infrastructure,
            Severity: SocSeverity.Info, Details: $"Variant build job {job.Id} was manually retried.",
            Source: nameof(VariantBuilderAdminController), GameId: job.Template.Challenge.GameId,
            ChallengeId: job.Template.ChallengeId,
            PayloadJson: System.Text.Json.JsonSerializer.Serialize(new { job.Id, job.TemplateId })));
        await db.SaveChangesAsync(token);
        return NoContent();
    }

    [HttpPost("templates/{templateId:long}/disable")]
    public async Task<IActionResult> Disable(long templateId, CancellationToken token)
    {
        var template = await db.VariantTemplates.Include(x => x.Challenge)
            .SingleOrDefaultAsync(x => x.Id == templateId, token);
        if (template is null)
            return NotFound();
        template.Status = VariantTemplateStatus.Disabled;
        template.UpdatedAtUtc = DateTimeOffset.UtcNow;
        socEvents.Enqueue(new SocEventMessage(
            EventType: "VariantTemplateDisabled", Category: SocCategory.PlatformSecurity,
            Severity: SocSeverity.Info, Details: $"Variant template {template.Version} was disabled.",
            Source: nameof(VariantBuilderAdminController), GameId: template.Challenge.GameId,
            ChallengeId: template.ChallengeId,
            PayloadJson: System.Text.Json.JsonSerializer.Serialize(new { template.Id, template.ManifestHash })));
        await db.SaveChangesAsync(token);
        return NoContent();
    }

    [HttpPost("challenges/{challengeId:int}/dataset/sync")]
    public async Task<ActionResult<RequestResponse<int>>> SyncDataset(int challengeId, CancellationToken token)
    {
        var challenge = await db.GameChallenges.SingleOrDefaultAsync(x => x.Id == challengeId, token);
        if (challenge is null || challenge.Type != ChallengeType.DynamicAttachment)
            return NotFound();
        var known = db.VariantDatasetItems.Where(x => x.ChallengeId == challengeId)
            .Select(x => x.FlagContextId);
        var flags = await db.FlagContexts.Include(x => x.Attachment).ThenInclude(x => x!.LocalFile)
            .Where(x => x.ChallengeId == challengeId && !known.Contains(x.Id))
            .ToArrayAsync(token);
        var items = flags.Select(x => new VariantDatasetItem
        {
            ChallengeId = challengeId,
            FlagContextId = x.Id,
            DatasetKey = $"flag-{x.Id}",
            ArtifactHash = x.Attachment?.LocalFile?.Hash,
            IsEnabled = !x.IsOccupied,
            SelectedAtUtc = x.IsOccupied ? DateTimeOffset.UtcNow : null
        }).ToArray();
        db.VariantDatasetItems.AddRange(items);
        socEvents.Enqueue(new SocEventMessage(
            EventType: "VariantDatasetSynchronized", Category: SocCategory.PlatformSecurity,
            Severity: SocSeverity.Info, Details: $"{items.Length} variant dataset items were indexed.",
            Source: nameof(VariantBuilderAdminController), GameId: challenge.GameId, ChallengeId: challengeId,
            PayloadJson: System.Text.Json.JsonSerializer.Serialize(new { count = items.Length })));
        await db.SaveChangesAsync(token);
        return Ok(new RequestResponse<int>("Dataset synchronized.", items.Length, StatusCodes.Status200OK));
    }
}
