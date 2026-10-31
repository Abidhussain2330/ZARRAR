using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace ZARRAR.Models.Data;

/// <summary>
/// Per-user interface preferences for the participant dashboard.
/// </summary>
/// <remarks>
/// These settings used to live in browser local storage only, which meant a participant had to
/// re-apply them on every device. Exactly one row exists per user, guaranteed by the unique index
/// over <see cref="UserId" />, and the row is removed with the user through the cascade on the
/// foreign key.
/// </remarks>
[Table("UserPreferences")]
[Index(nameof(UserId), IsUnique = true)]
[Comment("Stores per-user interface preferences for the participant dashboard.")]
public class UserPreferences
{
    [Key]
    [Comment("The unique identifier for the preference row.")]
    public int Id { get; set; }

    /// <summary>
    /// The ID of the user these preferences belong to.
    /// </summary>
    [Required]
    [Comment("The ID of the user these preferences belong to.")]
    public Guid UserId { get; set; }

    /// <summary>
    /// Navigation property for the owning user.
    /// </summary>
    [JsonIgnore]
    [ForeignKey(nameof(UserId))]
    public UserInfo User { get; set; } = null!;

    /// <summary>
    /// How much motion the interface should use.
    /// </summary>
    [Required]
    [Comment("How much motion the interface should use: 0 = system, 1 = full, 2 = reduced.")]
    public MotionPreference Motion { get; set; } = MotionPreference.System;

    /// <summary>
    /// Whether the low-energy presentation is enabled.
    /// </summary>
    [Required]
    [Comment("Whether the low-energy presentation is enabled.")]
    public bool LowEnergyMode { get; set; }

    /// <summary>
    /// Whether the navigation sidebar is collapsed.
    /// </summary>
    [Required]
    [Comment("Whether the navigation sidebar is collapsed.")]
    public bool SidebarCollapsed { get; set; }

    /// <summary>
    /// The time the preferences were last written.
    /// </summary>
    [Required]
    [Comment("The time the preferences were last written.")]
    public DateTimeOffset UpdateTimeUtc { get; set; } = DateTimeOffset.UtcNow;
}
