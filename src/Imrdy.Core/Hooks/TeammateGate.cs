using Imrdy.Core.State;
using Imrdy.Core.Status;

namespace Imrdy.Core.Hooks;

/// <summary>
/// Encapsulates the teammate gate for hook events.
/// <para>
/// Core invariant: <b>only the lead's own event stream determines whether the session is waiting
/// for the user.</b> Subagent activity says nothing about lead readiness — modern Claude Code runs
/// background agents that keep working after the lead has already returned control to the user.
/// Subagent events therefore only supply the running-task roster and, as the one exception, clear
/// a lead "permission" that the subagent itself resolved. They never move the lead status.
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
    /// Applies the teammate gate to an existing state file model.
    /// Clears a "permission" the subagent resolved. The lead's status is otherwise untouched — a
    /// subagent must never move the session off "waiting for the user".
    /// <para>
    /// <paramref name="roster"/> is orthogonal to that behavior. A non-null roster
    /// (including an empty one) overwrites <c>RunningTasks</c> outright — an empty list means
    /// "measured: nothing is running", which is a fact, not the absence of one. A <c>null</c>
    /// roster means the event said nothing about what is running, so the existing roster is left
    /// untouched. Permission-clearing behavior does not vary with any value of <paramref
    /// name="roster"/>.
    /// </para>
    /// </summary>
    public static StateFileModel ApplyTeammateEvent(
        StateFileModel existing,
        string hookEventName,
        IReadOnlyList<BackgroundTaskModel>? roster)
    {
        var now = DateTimeOffset.UtcNow;
        var updated = existing with
        {
            Timestamp = now,
            RunningTasks = roster ?? existing.RunningTasks,
        };

        if (ShouldClearPermission(existing.Status, hookEventName))
        {
            var clearedStatus = StatusDerivation.DeriveStatus(hookEventName);
            updated = updated with { Status = clearedStatus, NotificationType = "" };
        }

        return updated;
    }
}
