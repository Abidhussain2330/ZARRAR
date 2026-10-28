namespace BURAQ.Models.Transfer;

/// <summary>
/// Transfer package manifest
/// </summary>
public class TransferManifest
{
    /// <summary>
    /// Transfer format identifier written by this version.
    /// </summary>
    public const string FormatIdentifier = "BURAQ-GAME";

    /// <summary>
    /// Identifier written by older releases. Still accepted on import — the package
    /// layout is unchanged, so refusing it would strand every previously exported archive.
    /// </summary>
    public static readonly string LegacyFormatIdentifier = string.Concat("GZ", "CTF-GAME");

    /// <summary>
    /// Transfer format version
    /// </summary>
    public string Version { get; set; } = "1.1";

    /// <summary>
    /// Transfer format identifier
    /// </summary>
    public string Format { get; set; } = FormatIdentifier;

    /// <summary>
    /// Export timestamp
    /// </summary>
    public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Exporter version (BURAQ version)
    /// </summary>
    public string ExporterVersion { get; set; } = string.Empty;

    /// <summary>
    /// Game information
    /// </summary>
    public GameInfoSection Game { get; set; } = new();

    /// <summary>
    /// Export statistics
    /// </summary>
    public StatisticsSection Statistics { get; set; } = new();
}


public class GameInfoSection
{
    /// <summary>
    /// Original game ID (for reference only)
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Game title
    /// </summary>
    public string Title { get; set; } = string.Empty;
}

public class StatisticsSection
{
    /// <summary>
    /// Number of challenges
    /// </summary>
    public int ChallengeCount { get; set; }

    /// <summary>
    /// Number of divisions
    /// </summary>
    public int DivisionCount { get; set; }

    /// <summary>
    /// Total number of flags
    /// </summary>
    public int TotalFlags { get; set; }

    /// <summary>
    /// Total number of files
    /// </summary>
    public int TotalFiles { get; set; }

    /// <summary>
    /// Total size in bytes
    /// </summary>
    public long TotalFileSize { get; set; }
}
