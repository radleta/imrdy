namespace Imrdy.Core.Menus;

public sealed record ControllerMenuState
{
    public required IReadOnlyList<SessionMenuState> Sessions { get; init; }
    public required IReadOnlyList<WorkspaceMenuState> Workspaces { get; init; }
    public required IReadOnlyList<string> InstalledPacks { get; init; }
    public required ImrdyConfig Config { get; init; }
    public required string LogPath { get; init; }
}
