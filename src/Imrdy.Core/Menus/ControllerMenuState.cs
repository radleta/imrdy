using System.Drawing;

namespace Imrdy.Core.Menus;

public sealed record ControllerMenuState
{
    public required IReadOnlyList<SessionMenuState> Sessions { get; init; }
    public required IReadOnlyList<WorkspaceMenuState> Workspaces { get; init; }
    public required IReadOnlyList<string> InstalledPacks { get; init; }
    public required IReadOnlyList<string> InstalledGraphicsPacks { get; init; }
    public required IReadOnlyList<string> Monitors { get; init; }
    public required ImrdyConfig Config { get; init; }
    public required string LogPath { get; init; }

    /// <summary>
    /// Working area (logical px) of the monitor <c>Config.Overlay.Monitor</c> currently
    /// targets. Supplied by the Windows layer (<c>Screen.AllScreens</c>) so
    /// <c>ControllerMenuModel.BuildOverlaySubmenu</c> (Core) can resolve the position
    /// preset Checked-state via <c>OverlayPlacement.AnchorToOffset</c> without Core taking
    /// a <c>System.Windows.Forms.Screen</c> dependency (D7).
    /// </summary>
    public required Rectangle OverlayWorkingArea { get; init; }

    /// <summary>
    /// Current (or, when the overlay is disabled and no panel exists, estimated) overlay
    /// panel size (logical px) — the same <c>panelSize</c> basis
    /// <c>OverlayPlacement.AnchorToOffset</c> needs to resolve an anchor to an offset (D7).
    /// </summary>
    public required Size OverlayPanelSize { get; init; }

    /// <summary>
    /// Dev-build state. Null in prod (no <c>~/.imrdy/.dev-build</c> marker) — the Manage
    /// submenu hides its Dev sub-submenu entirely. Non-null in dev — the menu enumerates
    /// fixtures and exposes a Close-All item for unreachable preview windows.
    /// </summary>
    public DevBuildState? DevBuild { get; init; }
}

public sealed record DevBuildState
{
    public required IReadOnlyList<DevFixture> Fixtures { get; init; }

    /// <summary>Count of preview-dashboard processes the tray has launched and still considers alive.</summary>
    public required int RunningPreviewCount { get; init; }
}

public sealed record DevFixture(string DisplayName, string FullPath);
