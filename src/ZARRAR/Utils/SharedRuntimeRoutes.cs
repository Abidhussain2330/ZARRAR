namespace ZARRAR.Utils;

public static class SharedRuntimeRoutes
{
    public static string ForGame(int gameId, int challengeId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(gameId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(challengeId);
        return $"/api/gateway/{gameId}/{challengeId}/";
    }
}
