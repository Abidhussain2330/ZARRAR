using ZARRAR.Models.Request.Zarrar;

namespace ZARRAR.Hubs.Clients;

public interface IZarrarClient
{
    /// <summary>
    /// The showdown state changed (pause, resume, layout, end, mode).
    /// </summary>
    public Task DuelState(DuelStateModel state);

    /// <summary>
    /// Something happened that the stage should play: a flag, a release, a lost feed.
    /// </summary>
    public Task StageEvent(StageEventModel stageEvent);
}
