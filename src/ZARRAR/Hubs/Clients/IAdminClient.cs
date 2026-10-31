using ZARRAR.Models.Request.Admin;

namespace ZARRAR.Hubs.Clients;

public interface IAdminClient
{
    /// <summary>
    /// 接收到广播日志信息
    /// </summary>
    public Task ReceivedLog(LogMessageModel log);
    public Task ReceivedSocUpdate(SocUpdateModel update);
}
