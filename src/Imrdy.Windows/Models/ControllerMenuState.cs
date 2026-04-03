using Imrdy.Core.Sound;

namespace Imrdy.Windows.Models;

/// <summary>
/// Snapshot of in-memory state for the controller context menu.
/// Built by TrayApp on each menu open — no disk I/O.
/// </summary>
internal sealed record ControllerMenuState(
    IReadOnlyList<SessionEntry> Sessions,
    IReadOnlyList<WorkspaceSessionEntry> Workspaces,
    IReadOnlyList<string> InstalledPacks,
    SoundConfig Config,
    string ConfigDir,
    string SoundsDir,
    string LogPath,
    Action OnExit);
