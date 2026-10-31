namespace ZARRAR.Utils;

public static class ChallengeAttachmentRoutes
{
    public static string ForGame(int gameId, int challengeId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(gameId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(challengeId);
        return $"/api/game/{gameId}/challenges/{challengeId}/attachment";
    }
}

public static class ChallengeAssetAccess
{
    public static bool IsRestricted(bool isChallengeAsset, bool isAdministrator) =>
        isChallengeAsset && !isAdministrator;

    public static string CacheControl(bool isChallengeAsset) => isChallengeAsset
        ? "private, no-store, no-cache, max-age=0"
        : "public, max-age=604800";
}
