namespace Imrdy.Core;

/// <summary>
/// Root configuration record for ~/.imrdy/config.json.
/// Null sections are handled by ConfigReader.EnsureDefaults().
/// </summary>
public record ImrdyConfig
{
    public TrayConfig Tray { get; init; } = new();
    public SoundConfig Sound { get; init; } = new();
    public OverlayConfig Overlay { get; init; } = new();
    public DiagnosticsConfig Diagnostics { get; init; } = new();
}

public record TrayConfig
{
    public bool Enabled { get; init; } = true;
    public string IconStyle { get; init; } = "dots";
}

public record SoundConfig
{
    public bool Enabled { get; init; } = true;
    public string DefaultPack { get; init; } = "random";
    public List<string> DisabledPacks { get; init; } = [];
    public Dictionary<string, string> Projects { get; init; } = new();
}

public record OverlayConfig
{
    public bool Enabled { get; init; } = false;
    public string Position { get; init; } = "bottom-right";
    public int Size { get; init; } = 64;
    public int Spacing { get; init; } = 8;
    /// <summary>
    /// Zero-based index of the monitor the overlay docks to. 0 = primary monitor (default).
    /// </summary>
    public int Monitor { get; init; } = 0;
}

public record DiagnosticsConfig
{
    /// <summary>
    /// Nullable so ConfigReader.EnsureDefaults can distinguish "missing from JSON" (null) from "explicitly false".
    /// Resolution rule at runtime: <c>IpcEnabled ?? File.Exists(ImrdyPaths.DevBuildMarker)</c> — null defaults to on-in-dev, off-in-prod.
    /// Callers MUST NOT collapse null to a concrete bool in EnsureDefaults; the three-state semantics are intentional.
    /// </summary>
    public bool? IpcEnabled { get; init; } = null;
}
