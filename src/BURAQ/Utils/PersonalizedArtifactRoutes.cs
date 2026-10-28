namespace BURAQ.Utils;

public static class PersonalizedArtifactRoutes
{
    public static string ForGame(int gameId, int challengeId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(gameId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(challengeId);
        return $"/api/game/{gameId}/challenges/{challengeId}/artifact";
    }
}
