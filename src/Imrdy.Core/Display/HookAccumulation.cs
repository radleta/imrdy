namespace Imrdy.Core.Display;

/// <summary>
/// Read-only snapshot of per-session hook-derived state.
/// Produced by <see cref="Imrdy.Core.Hooks.HookAccumulationStore.GetSnapshot"/>.
/// </summary>
public sealed record HookAccumulation(
    int TurnCount,
    int FailureCount,
    IReadOnlyList<RecentToolEntry> RecentTools,
    IReadOnlyList<DateTimeOffset> ActivityTimestamps,
    IReadOnlySet<string> ActiveAgentIds,
    string? CurrentTool,
    string? PermissionTool
);

public sealed record RecentToolEntry(string ToolName, DateTimeOffset At);

public sealed record GitInfo(string Branch, int DirtyCount);

public sealed record RateLimits(string FiveHour, string SevenDay);
