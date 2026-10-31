using System.ComponentModel.DataAnnotations;

namespace ZARRAR.Models.Request.Dashboard;

/// <summary>
/// Per-user interface preferences, used both as the response and as the update payload
/// </summary>
public class UserPreferencesModel
{
    /// <summary>
    /// How much motion the interface should use
    /// </summary>
    /// <remarks>
    /// <see cref="EnumDataTypeAttribute" /> is required here: the enum converter accepts raw
    /// integers as well as names, so without it an undefined value would be persisted verbatim.
    /// </remarks>
    [Required]
    [EnumDataType(typeof(MotionPreference))]
    public MotionPreference Motion { get; set; } = MotionPreference.System;

    /// <summary>
    /// Whether the low-energy presentation is enabled
    /// </summary>
    [Required]
    public bool LowEnergyMode { get; set; }

    /// <summary>
    /// Whether the navigation sidebar is collapsed
    /// </summary>
    [Required]
    public bool SidebarCollapsed { get; set; }

    /// <summary>
    /// The defaults applied to a user who has never saved any preference.
    /// </summary>
    internal static UserPreferencesModel Default => new();

    internal static UserPreferencesModel FromPreferences(UserPreferences preferences) =>
        new()
        {
            Motion = preferences.Motion,
            LowEnergyMode = preferences.LowEnergyMode,
            SidebarCollapsed = preferences.SidebarCollapsed
        };
}
