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
    /// Applies the teammate gate to an existing state file model.
    /// Updates last_teammate_at timestamp, and clears permission status if applicable.
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

        return updated;
    }
}
