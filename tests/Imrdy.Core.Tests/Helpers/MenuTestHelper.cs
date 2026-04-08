using Imrdy.Core.Menus;

namespace Imrdy.Core.Tests.Helpers;

internal static class MenuTestHelper
{
    public static ControllerMenuState EmptyControllerState() => new()
    {
        Sessions = [],
        Workspaces = [],
        InstalledPacks = [],
        Config = new ImrdyConfig(),
        LogPath = @"C:\test\.imrdy\logs\monitor.log",
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
        Config = new ImrdyConfig { Sound = new SoundConfig { Enabled = true, DefaultPack = "random" } },
        LogPath = @"C:\test\.imrdy\logs\monitor.log",
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

    public static WorkspaceMenuState PinnedWorkspaceState(string name, string path) => new()
    {
        WorkspaceName = name,
        WorkspacePath = path,
    };
}
