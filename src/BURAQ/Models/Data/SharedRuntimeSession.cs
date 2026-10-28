using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace BURAQ.Models.Data;

/// <summary>Server-created participant lease for a shared challenge runtime.</summary>
[Index(nameof(SessionId), IsUnique = true)]
[Index(nameof(RuntimeConfigurationId), nameof(ParticipationId), IsUnique = true)]
[Index(nameof(ExpiresAtUtc))]
public sealed class SharedRuntimeSession
{
    [Key]
    public long Id { get; set; }

    public Guid SessionId { get; set; } = Guid.NewGuid();

    public long RuntimeConfigurationId { get; set; }

    public int ParticipationId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastAccessAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTimeOffset? RevokedAtUtc { get; set; }

    public SharedRuntimeConfiguration RuntimeConfiguration { get; set; } = null!;

    public Participation Participation { get; set; } = null!;
}
