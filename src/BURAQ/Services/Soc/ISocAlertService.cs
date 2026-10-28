namespace BURAQ.Services.Soc;

public interface ISocAlertService
{
    /// <summary>
    /// Creates a deduplicated SOC alert, emits the corresponding audit event, and broadcasts the
    /// update to connected administrators. Returns null when SourceKey already exists.
    /// </summary>
    Task<SocAlert?> CreateAsync(SocAlert alert, CancellationToken token = default);
}
