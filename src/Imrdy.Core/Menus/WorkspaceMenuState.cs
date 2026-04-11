namespace Imrdy.Core.Menus;

public sealed record WorkspaceMenuState
{
    public required string WorkspaceName { get; init; }
    public required string WorkspacePath { get; init; }
    public int DesktopIndex { get; init; }
    public int? DesktopCount { get; init; }
    public bool DesktopAvailable { get; init; }
    public string? IconStyle { get; init; }
    public IReadOnlyList<string> InstalledGraphicsPacks { get; init; } = [];
}
