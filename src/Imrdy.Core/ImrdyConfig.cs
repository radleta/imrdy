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
    public int Spacing { get; init; } = 4;
}
