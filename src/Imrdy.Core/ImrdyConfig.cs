namespace Imrdy.Core;

/// <summary>
/// Root configuration record for ~/.imrdy/config.json.
/// Null sections are handled by ConfigReader.EnsureDefaults().
/// </summary>
public record ImrdyConfig
{
    public TrayConfig Tray { get; init; } = new();
    public SoundConfig Sound { get; init; } = new();
}

public record TrayConfig
{
    public bool Enabled { get; init; } = true;
}

public record SoundConfig
{
    public bool Enabled { get; init; } = true;
    public string DefaultPack { get; init; } = "random";
    public List<string> DisabledPacks { get; init; } = [];
    public Dictionary<string, string> Projects { get; init; } = new();
}
