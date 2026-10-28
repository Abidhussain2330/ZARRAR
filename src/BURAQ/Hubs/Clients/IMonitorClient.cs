namespace BURAQ.Hubs.Clients;

public interface IMonitorClient
{
    /// <summary>
    /// 接收到比赛事件信息
    /// </summary>
    public Task ReceivedGameEvent(Models.Request.Game.GameEventInfoModel gameEvent);

    /// <summary>
    /// 接收到比赛提交信息
    /// </summary>
    public Task ReceivedSubmissions(Models.Request.Game.SubmissionInfoModel submission);
}
