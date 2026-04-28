namespace Imrdy.Core.Display;

/// <summary>
/// Serializable view model that drives the hover dashboard.
/// Phase 2 slots (ContextTokens, ContextWindowSize, CostUsd, ModelDisplayName, RateLimits)
/// are null in Phase 1; the dashboard renders them only when non-null.
/// </summary>
public sealed record DashboardViewModel(
    // Identity
    string SessionId,
    string SessionName,
    string Project,
    string CwdPath,
    int DesktopIndex,
    string? SoundPack,
    string? WslDistro,

    // Live state
    string Status,
    DateTimeOffset LastHookAt,
    DateTimeOffset StartedAt,

    // Accumulators (hook-derived)
    int TurnCount,
    int FailureCount,
    int SubagentCount,
    string? CurrentTool,
    IReadOnlyList<RecentToolEntry> RecentTools,
    IReadOnlyList<DateTimeOffset> ActivityTimestamps,
    string? LastPrompt,
    string? PermissionTool,

    // cwd-derived
    GitInfo? Git,

    // Fleet strip (mini-dot row above the focused-session detail)
    IReadOnlyList<FleetItem> FleetItems,

    // Phase 2 slots — always null until the statusLine subcommand lights them up
    int? ContextTokens,
    int? ContextWindowSize,
    decimal? CostUsd,
    string? ModelDisplayName,
    RateLimits? RateLimits
);

public sealed record FleetItem(
    string SessionId,
    string SessionName,
    string Status,
    bool IsHovered
);
