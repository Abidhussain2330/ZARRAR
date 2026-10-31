using System.Threading.Channels;
using ZARRAR.Models.Request.Admin;
using Microsoft.EntityFrameworkCore;

namespace ZARRAR.Services.Soc;

/// <summary>Persists every analysis request before using the in-memory channel only as a wake signal.</summary>
public sealed class SocDetectionQueue(AppDbContext db, ChannelWriter<SocDetectionEvent> wake)
{
    public async Task EnqueueAsync(int submissionId, CancellationToken token)
    {
        if (await db.SocDetectionJobs.AsNoTracking().AnyAsync(x => x.SubmissionId == submissionId, token))
        {
            wake.TryWrite(new SocDetectionEvent(submissionId));
            return;
        }

        var job = new SocDetectionJob { SubmissionId = submissionId };
        db.SocDetectionJobs.Add(job);
        try
        {
            await db.SaveChangesAsync(token);
        }
        catch (DbUpdateException)
        {
            db.Entry(job).State = EntityState.Detached;
            if (!await db.SocDetectionJobs.AsNoTracking().AnyAsync(x => x.SubmissionId == submissionId, token))
                throw;
        }
        wake.TryWrite(new SocDetectionEvent(submissionId));
    }
}
