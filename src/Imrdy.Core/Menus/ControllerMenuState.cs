using Imrdy.Core.Sound;

namespace Imrdy.Core.Menus;

public sealed record ControllerMenuState
{
    public required IReadOnlyList<SessionMenuState> Sessions { get; init; }
    public required IReadOnlyList<WorkspaceMenuState> Workspaces { get; init; }
    public required IReadOnlyList<string> InstalledPacks { get; init; }
    public required SoundConfig Config { get; init; }
    public required string ConfigDir { get; init; }
    public required string SoundsDir { get; init; }
    public required string LogPath { get; init; }
}
