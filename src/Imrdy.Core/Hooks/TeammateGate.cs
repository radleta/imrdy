using Imrdy.Core.State;
using Imrdy.Core.Status;

namespace Imrdy.Core.Hooks;

/// <summary>
/// Encapsulates the teammate gate for hook events.
/// <para>
/// Core invariant: <b>only the lead's own event stream determines whether the session is waiting
/// for the user.</b> Subagent activity says nothing about lead readiness — modern Claude Code runs
/// background agents that keep working after the lead has already returned control to the user.
/// Subagent events therefore only refresh <c>last_teammate_at</c> (liveness, used for icon aging),
/// with one exception: they clear a lead "permission" that the subagent itself resolved.
/// </para>
/// </summary>
public static class TeammateGate
{
    /// <summary>
    /// Events that indicate a permission prompt has been resolved (approved or denied).
    /// </summary>
    private static readonly HashSet<string> PermissionResolutionEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        "PostToolUse",
        "PostToolUseFailure",
        "PermissionDenied",
    };

    /// <summary>
    /// Subagent events that mark work <b>ending</b> rather than work happening. They must not
    /// refresh <c>last_teammate_at</c>: that field answers "when was a subagent last doing work",
    /// and a stop is the moment work ceased. Refreshing on a terminal event holds the session teal
    /// for a further 2 minutes after the last agent has already finished, and a stray one — an
    /// observed <c>SubagentStop</c> with an empty <c>agent_type</c> and no matching
    /// <c>SubagentStart</c> — invents agent activity for a session that never had any.
    /// </summary>
    private static readonly HashSet<string> TerminalActivityEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        "SubagentStop",
        "TaskCompleted",
        "TeammateIdle",
        "Stop",
    };

    /// <summary>
    /// Subagent lifecycle events. These describe a subagent starting, finishing, or going idle —
    /// never whether the lead is waiting for the user. They may arrive on the lead's stream without
    /// an <c>agent_id</c> (the parent spawns and reaps the subagent), so they must be filtered on
    /// the lead path too, not just by the <c>agent_id</c> gate.
    /// </summary>
    private static readonly HashSet<string> SubagentLifecycleEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        "SubagentStart",
        "SubagentStop",
        "TaskCreated",
        "TaskCompleted",
        "TeammateIdle",
    };

    /// <summary>
    /// Determines whether a teammate event should clear the lead's "permission" status.
    /// Returns true when the lead is stuck at "permission" and the teammate fires
    /// a post-permission event (PostToolUse, PostToolUseFailure, PermissionDenied).
    /// </summary>
    public static bool ShouldClearPermission(string? existingStatus, string hookEventName)
    {
        return existingStatus == "permission"
            && PermissionResolutionEvents.Contains(hookEventName);
    }

    /// <summary>
    /// True when the event describes subagent lifecycle rather than lead readiness.
    /// Callers on the lead path must preserve the existing status for these events.
    /// </summary>
    public static bool IsSubagentLifecycleEvent(string hookEventName)
    {
        return SubagentLifecycleEvents.Contains(hookEventName);
    }

    /// <summary>
    /// True when the event means a subagent is actively working, as opposed to reporting that it
    /// has stopped. Only ongoing activity refreshes the liveness window.
    /// </summary>
    public static bool IsOngoingActivity(string hookEventName)
    {
        return !TerminalActivityEvents.Contains(hookEventName);
    }

    /// <summary>
    /// Applies the teammate gate to an existing state file model.
    /// Refreshes <c>last_teammate_at</c> on ongoing activity so the tray can keep the icon lively
    /// while subagents work, and clears a "permission" the subagent resolved. The lead's status is
    /// otherwise untouched — a subagent must never move the session off "waiting for the user".
    /// </summary>
    public static StateFileModel ApplyTeammateEvent(StateFileModel existing, string hookEventName)
    {
        var now = DateTimeOffset.UtcNow;
        var updated = existing with
        {
            LastTeammateAt = IsOngoingActivity(hookEventName) ? now : existing.LastTeammateAt,
            Timestamp = now,
        };

        if (ShouldClearPermission(existing.Status, hookEventName))
        {
            var clearedStatus = StatusDerivation.DeriveStatus(hookEventName);
            updated = updated with { Status = clearedStatus, NotificationType = "" };
        }

        return updated;
    }
}
