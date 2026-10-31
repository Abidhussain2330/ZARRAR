namespace ZARRAR.Services.Soc;

public interface ISocEventPublisher
{
    /// <summary>
    /// Adds an event to the current AppDbContext unit of work. The caller's next SaveChanges call
    /// persists the event atomically with the platform mutation.
    /// </summary>
    Guid Enqueue(SocEventMessage message);

    /// <summary>
    /// Adds and immediately persists an event. Use this only when the platform mutation has already
    /// committed or no surrounding database transaction exists.
    /// </summary>
    Task<Guid> PublishAsync(SocEventMessage message, CancellationToken token = default);
}
