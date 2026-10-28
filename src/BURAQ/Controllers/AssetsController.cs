using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using System.Security.Claims;
using BURAQ.Middlewares;
using BURAQ.Models;
using BURAQ.Repositories.Interface;
using BURAQ.Storage.Interface;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Localization;
using Microsoft.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;

namespace BURAQ.Controllers;

/// <summary>
/// File APIs
/// </summary>
[ApiController]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
public class AssetsController(
    IBlobStorage storage,
    IBlobRepository blobService,
    AppDbContext db,
    ILogger<AssetsController> logger,
    IStringLocalizer<Program> localizer) : ControllerBase
{
    private readonly FileExtensionContentTypeProvider _extProvider = new();

    /// <summary>
    /// File retrieval interface
    /// </summary>
    /// <remarks>
    /// Retrieve a file by hash, filename is not matched
    /// </remarks>
    /// <param name="hash">File hash</param>
    /// <param name="filename">Download filename</param>
    /// <param name="token"></param>
    /// <response code="200">File retrieved successfully</response>
    /// <response code="404">File not found</response>
    /// <response code="400">Failed to retrieve file</response>
    [HttpGet("[controller]/{hash:length(64)}/{filename:minlength(1)}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetFile([RegularExpression("[0-9a-f]{64}")] string hash, string filename,
        CancellationToken token)
    {
        var isChallengeAsset = await db.GameChallenges.AsNoTracking().AnyAsync(c =>
                c.Attachment != null && c.Attachment.LocalFile != null && c.Attachment.LocalFile.Hash == hash, token) ||
            await db.FlagContexts.AsNoTracking().AnyAsync(f =>
                f.Attachment != null && f.Attachment.LocalFile != null && f.Attachment.LocalFile.Hash == hash, token);

        // Challenge downloads must pass through a game-scoped endpoint so participation and
        // ownership are checked. Administrators retain this route for the attachment preview in
        // the challenge editor. Return 404 to other callers without confirming that the hash is valid.
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var isAdministrator = Guid.TryParse(userId, out var parsedUserId) &&
            await db.Users.AsNoTracking().AnyAsync(u => u.Id == parsedUserId && u.Role >= Role.Admin, token);

        // This route serves both public blobs and administrator-only challenge previews. A public
        // cache directive on the action allowed a browser/shared cache to replay an administrator's
        // successful challenge preview to a later anonymous request without reaching this access
        // check. Challenge-backed responses (including the indistinguishable 404 denial) must never
        // enter a shared cache; ordinary non-challenge blobs retain the historical one-week cache.
        Response.Headers.CacheControl = ChallengeAssetAccess.CacheControl(isChallengeAsset);
        if (isChallengeAsset)
        {
            Response.Headers.Pragma = "no-cache";
            Response.Headers.Expires = "0";
            Response.Headers.Vary = "Cookie, Authorization";
        }

        if (ChallengeAssetAccess.IsRestricted(isChallengeAsset, isAdministrator))
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.File_NotFound)],
                StatusCodes.Status404NotFound));

        var path = StoragePath.Combine(PathHelper.Uploads, hash[..2], hash[2..4], hash);

        if (!await storage.ExistsAsync(path, token))
        {
            var ip = HttpContext.Connection.RemoteIpAddress;
            logger.Log(StaticLocalizer[nameof(Resources.Program.Assets_FileNotFound), hash[..8], filename], ip,
                TaskStatus.NotFound,
                LogLevel.Warning);
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.File_NotFound)],
                StatusCodes.Status404NotFound));
        }

        if (!_extProvider.TryGetContentType(filename, out var contentType))
            contentType = MediaTypeNames.Application.Octet;

        var blob = await storage.GetBlobAsync(path, token);

        var stream = await storage.OpenReadAsync(path, token);
        var etag = new EntityTagHeaderValue($"\"{hash[8..16]}\"");

        return File(stream, contentType, filename, blob.LastModificationTime, etag);
    }

    /// <summary>
    /// File upload interface
    /// </summary>
    /// <remarks>
    /// Upload one or more files
    /// </remarks>
    /// <param name="files"></param>
    /// <param name="filename">Unified filename</param>
    /// <param name="token"></param>
    /// <response code="200">File(s) uploaded successfully</response>
    /// <response code="400">Failed to upload file(s)</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Access denied</response>
    [RequireAdmin]
    [HttpPost("api/[controller]")]
    [ProducesResponseType(typeof(List<LocalFile>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    // Was long.MaxValue, which let any administrator exhaust the server's disk or memory with a
    // single request (finding F-014).
    [RequestFormLimits(MultipartBodyLengthLimit = Limits.MaxAssetUploadSize)]
    public async Task<IActionResult> Upload(List<IFormFile> files, [FromQuery] string? filename,
        CancellationToken token)
    {
        if (files.Sum(file => file.Length) > Limits.MaxAssetUploadSize)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.File_SizeTooLarge)]));

        try
        {
            List<LocalFile> results = [];
            foreach (var file in files.Where(file => file.Length > 0))
            {
                var res = await blobService.CreateOrUpdateBlob(file, filename, token);
                logger.SystemLog(
                    StaticLocalizer[nameof(Resources.Program.Assets_UpdateFile), res.Hash[..8],
                        filename ?? file.FileName, file.Length],
                    TaskStatus.Success, LogLevel.Debug);
                results.Add(res);
            }

            return Ok(results);
        }
        catch (Exception ex)
        {
            logger.LogErrorMessage(ex, ex.Message);
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Assets_IOError)]));
        }
    }

    /// <summary>
    /// File deletion interface
    /// </summary>
    /// <remarks>
    /// Delete a file by hash
    /// </remarks>
    /// <param name="hash"></param>
    /// <param name="token"></param>
    /// <response code="200">File deleted successfully</response>
    /// <response code="400">Failed to delete file</response>
    /// <response code="401">Unauthorized user</response>
    /// <response code="403">Access denied</response>
    [RequireAdmin]
    [HttpDelete("api/[controller]/{hash:length(64)}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Delete(string hash, CancellationToken token)
    {
        var result = await blobService.DeleteBlobByHash(hash, token);

        logger.SystemLog(StaticLocalizer[nameof(Resources.Program.Assets_DeleteFile), hash[..8]], result,
            LogLevel.Information);

        return result switch
        {
            TaskStatus.Success => Ok(),
            TaskStatus.NotFound => NotFound(),
            _ => BadRequest(new RequestResponse(localizer[nameof(Resources.Program.File_DeletionFailed)]))
        };
    }
}
