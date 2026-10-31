using Microsoft.EntityFrameworkCore;

namespace ZARRAR.Services.Variants;

public sealed class VariantDatasetSelector(AppDbContext db, DeterministicSeedService seeds)
{
    /// <summary>Selects one currently free item with a stable keyed ranking for this participation.</summary>
    public async Task<VariantDatasetItem?> SelectAsync(int challengeId, int participationId,
        DateTimeOffset now, CancellationToken token)
    {
        var candidates = await db.VariantDatasetItems
            .Include(x => x.FlagContext)
            .ThenInclude(x => x.Attachment)
            .ThenInclude(x => x!.LocalFile)
            .Where(x => x.ChallengeId == challengeId && x.IsEnabled && x.SelectedAtUtc == null &&
                !x.FlagContext.IsOccupied)
            .ToArrayAsync(token);
        var selected = SelectCandidate(candidates, seeds, challengeId, participationId);
        if (selected is null)
            return null;

        selected.SelectedAtUtc = now;
        selected.SelectedParticipationId = participationId;
        selected.FlagContext.IsOccupied = true;
        return selected;
    }

    /// <summary>
    /// Deterministic builders use the configured keyed ranking. The original manually managed
    /// personalized-attachment pool remains available when that optional integration is disabled;
    /// its advisory-locked allocator falls back to the stable database order.
    /// </summary>
    internal static VariantDatasetItem? SelectCandidate(IReadOnlyCollection<VariantDatasetItem> candidates,
        DeterministicSeedService seeds, int challengeId, int participationId) =>
        seeds.IsConfigured
            ? candidates.MinBy(x => seeds.Rank(challengeId, participationId, x.DatasetKey),
                StringComparer.Ordinal)
            : candidates.MinBy(x => x.Id);
}
