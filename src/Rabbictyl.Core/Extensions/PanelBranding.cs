namespace Rabbictyl.Core.Extensions;

/// <summary>
/// Operator-controlled branding stored next to the panel state, so the panel
/// can be renamed and re-logoed without editing the TOML configuration.
/// </summary>
public sealed class PanelBranding
{
    public string Name { get; set; } = string.Empty;

    public bool HideSidebarLogo { get; set; }

    /// <summary>File name inside the storage directory, or null for the bundled logo.</summary>
    public string? LogoFileName { get; set; }

    /// <summary>When set, everyone but administrators sees the maintenance page.</summary>
    public bool MaintenanceMode { get; set; }

    /// <summary>Reason shown on the maintenance page.</summary>
    public string? MaintenanceReason { get; set; }
}
