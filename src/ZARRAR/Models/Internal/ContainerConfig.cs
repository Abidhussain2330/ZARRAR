namespace ZARRAR.Models.Internal;

public class ContainerConfig
{
    /// <summary>
    /// Container image
    /// </summary>
    public string Image { get; set; } = string.Empty;

    /// <summary>
    /// Team ID
    /// </summary>
    public string TeamId { get; set; } = string.Empty;

    /// <summary>
    /// Challenge ID
    /// </summary>
    public int ChallengeId { get; set; }

    /// <summary>
    /// Game ID, null for exercise containers
    /// </summary>
    public int? GameId { get; set; }

    /// <summary>
    /// User ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Port to be exposed by the container
    /// </summary>
    public int ExposedPort { get; set; }

    /// <summary>
    /// Flag text
    /// </summary>
    public string? Flag { get; set; } = string.Empty;

    /// <summary>
    /// Whether to record traffic
    /// </summary>
    public bool EnableTrafficCapture { get; set; }

    /// <summary>
    /// Memory limit (MB)
    /// </summary>
    public int MemoryLimit { get; set; } = 64;

    /// <summary>
    /// CPU limit (0.1 CPUs)
    /// </summary>
    public int CPUCount { get; set; } = 1;

    /// <summary>
    /// Storage write limit
    /// </summary>
    public int StorageLimit { get; set; } = 256;

    /// <summary>
    /// Container network mode
    /// </summary>
    /// <remarks>
    /// Defence in depth for finding F-020. Every construction site sets this explicitly from the
    /// challenge or the configured default, so this initialiser should never decide anything — but
    /// if a future call site forgets, it must fail closed rather than launch an open container.
    /// </remarks>
    public NetworkMode NetworkMode { get; set; } = NetworkMode.Isolated;
}
