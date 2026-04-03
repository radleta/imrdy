using Imrdy.Core.Menus;
using Imrdy.Core.Sound;

namespace Imrdy.Core.Tests.Helpers;

internal static class MenuTestHelper
{
    public static ControllerMenuState EmptyControllerState() => new()
    {
        Sessions = [],
        Workspaces = [],
        InstalledPacks = [],
        Config = new SoundConfig { SoundEnabled = true },
        ConfigDir = @"C:\test\.imrdy",
        SoundsDir = @"C:\test\.claude\sounds",
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
        Config = new SoundConfig { SoundEnabled = true, Default = "assistant" },
        ConfigDir = @"C:\test\.imrdy",
        SoundsDir = @"C:\test\.claude\sounds",
        LogPath = @"C:\test\.imrdy\logs\monitor.log",
    };

    public static ControllerMenuState SoundDisabledControllerState()
    {
        var active = ActiveControllerState();
        return active with { Config = active.Config with { SoundEnabled = false } };
    }

    public static SessionMenuState SingleSessionState(string? project, string status) => new()
    {
        SessionId = "test-session",
        Status = status,
        Project = project,
    };

    public static WorkspaceMenuState PinnedWorkspaceState(string name, string path) => new()
    {
        WorkspaceName = name,
        WorkspacePath = path,
    };
}
