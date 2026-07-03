using System.Drawing;
using Imrdy.Core.Menus;

namespace Imrdy.Core.Tests.Helpers;

internal static class MenuTestHelper
{
    /// <summary>
    /// Representative 1920×1040 working area (1920×1080 minus an 8px auto-hide-taskbar
    /// reserve zone, though the exact reserve isn't material here) and a round 200×72
    /// panel size — used by <see cref="ControllerMenuState.OverlayWorkingArea"/>/
    /// <see cref="ControllerMenuState.OverlayPanelSize"/> so overlay position-preset
    /// Checked-state tests (D7) have deterministic, easy-to-hand-verify geometry.
    /// </summary>
    public static readonly Rectangle DefaultOverlayWorkingArea = new(0, 0, 1920, 1040);
    public static readonly Size DefaultOverlayPanelSize = new(200, 72);

    public static ControllerMenuState EmptyControllerState() => new()
    {
        Sessions = [],
        Workspaces = [],
        InstalledPacks = [],
        InstalledGraphicsPacks = [],
        Monitors = [],
        Config = new ImrdyConfig(),
        LogPath = @"C:\test\.imrdy\logs\monitor.log",
        OverlayWorkingArea = DefaultOverlayWorkingArea,
        OverlayPanelSize = DefaultOverlayPanelSize,
    };

    public static ControllerMenuState ActiveControllerState() => new()
    {
        Sessions =
        [
            new SessionMenuState { SessionId = "s1", Status = "idle", Project = "project-a" },
            new SessionMenuState { SessionId = "s2", Status = "busy", Project = "project-b" },
            new SessionMenuState { SessionId = "s3", Status = "needs_you", Project = "project-c" },
        ],
        Workspaces =
        [
            new WorkspaceMenuState { WorkspaceName = "Dev", WorkspacePath = @"C:\dev" },
        ],
        InstalledPacks = ["assistant", "retro"],
        InstalledGraphicsPacks = [],
        Monitors = [],
        Config = new ImrdyConfig { Sound = new SoundConfig { Enabled = true, DefaultPack = "random" } },
        LogPath = @"C:\test\.imrdy\logs\monitor.log",
        OverlayWorkingArea = DefaultOverlayWorkingArea,
        OverlayPanelSize = DefaultOverlayPanelSize,
    };

    public static ControllerMenuState SoundDisabledControllerState()
    {
        var active = ActiveControllerState();
        return active with { Config = new ImrdyConfig { Sound = active.Config.Sound with { Enabled = false } } };
    }

    public static SessionMenuState SingleSessionState(string? project, string status) => new()
    {
        SessionId = "test-session",
        Status = status,
        Project = project,
        DesktopAvailable = true,
        DesktopCount = 3,
        DesktopIndex = 0,
        InstalledPacks = ["assistant", "retro"],
        SoundPack = "assistant",
    };

    public static SessionMenuState SessionNoDesktop(string? project = "proj", string status = "idle") => new()
    {
        SessionId = "test-session",
        Status = status,
        Project = project,
        DesktopAvailable = false,
    };

    public static SessionMenuState SessionNoPacks(string? project = "proj", string status = "idle") => new()
    {
        SessionId = "test-session",
        Status = status,
        Project = project,
        DesktopAvailable = true,
        DesktopCount = 2,
    };

    public static WorkspaceMenuState PinnedWorkspaceState(
        string name,
        string path,
        string? iconStyle = null,
        IReadOnlyList<string>? installedGraphicsPacks = null) => new()
    {
        WorkspaceName = name,
        WorkspacePath = path,
        IconStyle = iconStyle,
        InstalledGraphicsPacks = installedGraphicsPacks ?? [],
    };

    public static SessionMenuState SessionWithDesktopIndex(
        string sessionId,
        int? desktopIndex,
        string status = "idle",
        string? project = null) => new()
    {
        SessionId = sessionId,
        Status = status,
        Project = project ?? sessionId,
        DesktopIndex = desktopIndex,
    };

    public static WorkspaceMenuState WorkspaceWithDesktopIndex(
        string name,
        string path,
        int desktopIndex) => new()
    {
        WorkspaceName = name,
        WorkspacePath = path,
        DesktopIndex = desktopIndex,
    };
}
