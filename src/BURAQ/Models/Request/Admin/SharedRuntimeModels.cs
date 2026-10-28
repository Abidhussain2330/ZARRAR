using System.ComponentModel.DataAnnotations;

namespace BURAQ.Models.Request.Admin;

public sealed class SharedRuntimeUpdateModel
{
    [Required]
    [MaxLength(2048)]
    public string Origin { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^[a-zA-Z0-9._:-]{1,128}$")]
    public string Audience { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^(?!//)(?!.*//)(?!/(?:\\.{1,2})(?:/|$))(?!.*(?:/\\.{1,2})(?:/|$))/[a-zA-Z0-9._~!$&'()*+,;=:@/-]{0,127}$")]
    public string HealthPath { get; set; } = "/healthz";

    public SharedRuntimeStateMode StateMode { get; set; }

    [Range(1, 100000)]
    public int MaxConcurrentSessions { get; set; } = 1000;

    public bool IsEnabled { get; set; } = true;
}

public sealed record SharedRuntimeStatusModel(
    int ChallengeId,
    string Challenge,
    string Origin,
    string Audience,
    string HealthPath,
    SharedRuntimeStateMode StateMode,
    bool IsEnabled,
    int ActiveSessions,
    int MaxConcurrentSessions,
    string HealthStatus,
    DateTimeOffset? LastHealthCheckUtc,
    string? LastHealthError,
    DateTimeOffset UpdatedAtUtc);
