namespace Imrdy.Core.Menus;

public sealed record SessionMenuState
{
    public required string SessionId { get; init; }
    public required string Status { get; init; }
    public string? Project { get; init; }
    public int? DesktopIndex { get; init; }
    public string? SoundPack { get; init; }
    public IReadOnlyList<string> InstalledPacks { get; init; } = [];
    public int? DesktopCount { get; init; }
    public bool DesktopAvailable { get; init; }
    public bool IsPinned { get; init; }
}
