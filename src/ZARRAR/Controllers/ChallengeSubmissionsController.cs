using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using ZARRAR.Middlewares;
using ZARRAR.Models;
using ZARRAR.Models.Data;
using ZARRAR.Models.Request;
using ZARRAR.Repositories.Interface;
using ZARRAR.Storage.Interface;
using ZARRAR.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ZARRAR.Controllers;

/// <summary>
/// Browser-based challenge package submissions for approved challenge authors.
/// Packages are private until an administrator reviews them.
/// </summary>
[ApiController]
public class ChallengeSubmissionsController(
    AppDbContext db,
    IBlobRepository blobs,
    IBlobStorage storage,
    ILogger<ChallengeSubmissionsController> logger) : ControllerBase
{
    private static readonly string[] AllowedExtensions = [".zip", ".tar", ".gz", ".tgz"];

    [RequireChallengeDeveloper]
    [HttpGet("api/challenge-submissions/mine")]
    public async Task<IActionResult> Mine(CancellationToken token)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        var items = await db.ChallengeSubmissions.AsNoTracking()
            .Where(x => x.DeveloperId == userId.Value)
            .OrderByDescending(x => x.SubmittedUtc)
            .ToArrayAsync(token);
        return Ok(items.Select(ChallengeSubmissionInfo.From));
    }

    [RequireChallengeDeveloper]
    [HttpPost("api/challenge-submissions")]
    [RequestFormLimits(MultipartBodyLengthLimit = Limits.MaxAssetUploadSize)]
    public async Task<IActionResult> Submit(
        [FromForm, Required, StringLength(128)] string title,
        [FromForm, StringLength(4000)] string? description,
        [FromForm, Required] IFormFile package,
        CancellationToken token)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        if (package.Length <= 0 || package.Length > Limits.MaxAssetUploadSize)
            return BadRequest(new RequestResponse("The package must be between 1 byte and 256 MB."));

        var name = Path.GetFileName(package.FileName);
        var extension = Path.GetExtension(name).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension) ||
            (extension == ".gz" && !name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)))
            return BadRequest(new RequestResponse("Only .zip, .tar, and .tar.gz challenge packages are accepted."));

        var file = await blobs.CreateOrUpdateBlob(package, name, token);
        var item = new ChallengeSubmission
        {
            Title = title.Trim(),
            Description = description?.Trim() ?? string.Empty,
            PackageName = name,
            PackageHash = file.Hash,
            PackageSize = file.FileSize,
            DeveloperId = userId.Value
        };
        db.ChallengeSubmissions.Add(item);
        await db.SaveChangesAsync(token);
        logger.LogInformation("Challenge package {SubmissionId} submitted by {DeveloperId}", item.Id, userId);
        return Ok(ChallengeSubmissionInfo.From(item));
    }

    [RequireAdmin]
    [HttpGet("api/admin/challenge-submissions")]
    public async Task<IActionResult> All(CancellationToken token)
    {
        var items = await db.ChallengeSubmissions.AsNoTracking()
            .Include(x => x.Developer)
            .OrderByDescending(x => x.SubmittedUtc)
            .ToArrayAsync(token);
        return Ok(items.Select(ChallengeSubmissionInfo.From));
    }

    [RequireAdmin]
    [HttpPost("api/admin/challenge-submissions/{id:guid}/review")]
    public async Task<IActionResult> Review(Guid id, [FromBody] ChallengeSubmissionReviewModel model,
        CancellationToken token)
    {
        if (model.Status is not ChallengeSubmissionStatus.Approved and not ChallengeSubmissionStatus.Rejected)
            return BadRequest(new RequestResponse("A review must approve or reject the submission."));

        var item = await db.ChallengeSubmissions.SingleOrDefaultAsync(x => x.Id == id, token);
        if (item is null) return NotFound();
        item.Status = model.Status;
        item.ReviewNote = model.Note?.Trim() ?? string.Empty;
        item.ReviewedUtc = DateTimeOffset.UtcNow;
        item.ReviewerId = CurrentUserId();
        await db.SaveChangesAsync(token);
        return Ok(ChallengeSubmissionInfo.From(item));
    }

    [RequireChallengeDeveloper]
    [HttpGet("api/challenge-submissions/{id:guid}/package")]
    public async Task<IActionResult> Download(Guid id, CancellationToken token)
    {
        var item = await db.ChallengeSubmissions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token);
        if (item is null) return NotFound();
        var userId = CurrentUserId();
        var isAdmin = await db.Users.AsNoTracking().Where(x => x.Id == userId).Select(x => x.Role == Role.Admin).SingleOrDefaultAsync(token);
        if (!isAdmin && item.DeveloperId != userId) return Forbid();

        var path = StoragePath.Combine(PathHelper.Uploads, item.PackageHash[..2], item.PackageHash[2..4], item.PackageHash);
        if (!await storage.ExistsAsync(path, token)) return NotFound();
        var stream = await storage.OpenReadAsync(path, token);
        return File(stream, "application/octet-stream", item.PackageName, enableRangeProcessing: true);
    }

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
