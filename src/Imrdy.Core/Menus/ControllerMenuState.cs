namespace Imrdy.Core.Menus;

public sealed record ControllerMenuState
{
    public required IReadOnlyList<SessionMenuState> Sessions { get; init; }
    public required IReadOnlyList<WorkspaceMenuState> Workspaces { get; init; }
    public required IReadOnlyList<string> InstalledPacks { get; init; }
    public required IReadOnlyList<string> InstalledGraphicsPacks { get; init; }
    public required ImrdyConfig Config { get; init; }
    public required string LogPath { get; init; }

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
