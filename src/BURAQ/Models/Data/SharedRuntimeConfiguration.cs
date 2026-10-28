using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace BURAQ.Models.Data;

[JsonConverter(typeof(JsonStringEnumConverter<SharedRuntimeStateMode>))]
public enum SharedRuntimeStateMode : byte
{
    Stateless = 0,
    TeamNamespaced = 1
}

/// <summary>Administrator-owned routing and isolation policy for one shared challenge runtime.</summary>
[Index(nameof(ChallengeId), IsUnique = true)]
public sealed class SharedRuntimeConfiguration
{
    [Key]
    public long Id { get; set; }

    public int ChallengeId { get; set; }

    [Required]
    [MaxLength(2048)]
    public string Origin { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string Audience { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string HealthPath { get; set; } = "/healthz";

    public SharedRuntimeStateMode StateMode { get; set; }

    public bool IsEnabled { get; set; } = true;

    [Range(1, 100000)]
    public int MaxConcurrentSessions { get; set; } = 1000;

    [MaxLength(32)]
    public string HealthStatus { get; set; } = "Unknown";

    [MaxLength(1000)]
    public string? LastHealthError { get; set; }

    public DateTimeOffset? LastHealthCheckUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public GameChallenge Challenge { get; set; } = null!;

    public List<SharedRuntimeSession> Sessions { get; set; } = [];
}
