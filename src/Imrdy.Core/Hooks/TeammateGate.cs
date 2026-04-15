using Imrdy.Core.State;
using Imrdy.Core.Status;

namespace Imrdy.Core.Hooks;

/// <summary>
/// Encapsulates the teammate gate logic for hook events.
/// Teammate events (with agent_id) normally preserve the lead's status,
/// but must clear "permission" when the permission has been resolved.
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
    /// Lead statuses that appear idle (green icon) but should show busy when teammates are working.
    /// "done" is excluded — consensus promotion handles that path separately.
    /// </summary>
    private static readonly HashSet<string> IdleLeadStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "start",
        "idle",
    };

    /// <summary>
    /// Teammate events that indicate active work (tool use, subagent activity).
    /// </summary>
    private static readonly HashSet<string> BusyTeammateEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        "PreToolUse",
        "PostToolUse",
        "PostToolUseFailure",
        "SubagentStart",
        "SubagentStop",
        "WorktreeCreate",
        "UserPromptSubmit",
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
    /// Determines whether a teammate event should promote the lead from an idle status to busy.
    /// Returns true when the lead is at an idle-equivalent status (start, idle) and the teammate
    /// fires a work event, indicating the session is actively working via teammates.
    /// </summary>
    public static bool ShouldPromoteToBusy(string? existingStatus, string hookEventName)
    {
        return existingStatus is not null
            && IdleLeadStatuses.Contains(existingStatus)
            && BusyTeammateEvents.Contains(hookEventName);
    }

    /// <summary>
    /// Applies the teammate gate to an existing state file model.
    /// Updates last_teammate_at timestamp. May also change lead status:
    /// - Clears "permission" when a resolution event fires (PostToolUse, PermissionDenied, etc.)
    /// - Promotes idle leads (start, idle) to "busy" when teammates are doing work
    /// Returns the updated state file model ready for writing.
    /// </summary>
    public static StateFileModel ApplyTeammateEvent(StateFileModel existing, string hookEventName)
    {
        var now = DateTimeOffset.UtcNow;
        var updated = existing with
        {
            LastTeammateAt = now,
            Timestamp = now,
        };

        if (ShouldClearPermission(existing.Status, hookEventName))
        {
            var clearedStatus = StatusDerivation.DeriveStatus(hookEventName);
            updated = updated with { Status = clearedStatus, NotificationType = "" };
        }
        else if (ShouldPromoteToBusy(existing.Status, hookEventName))
        {
            updated = updated with { Status = "busy" };
        }

        return updated;
    }
}
