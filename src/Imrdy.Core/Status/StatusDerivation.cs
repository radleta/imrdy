namespace Imrdy.Core.Status;

/// <summary>
/// Derives session status from hook event name and context.
/// Port of deriveStatus() from hook-lib.mjs.
/// </summary>
public static class StatusDerivation
{
    /// <summary>
    /// Lead-readiness map. Only events that carry information about whether the MAIN session is
    /// waiting for the user may appear here — subagent lifecycle events are handled by
    /// <see cref="Imrdy.Core.Hooks.TeammateGate"/> and never mutate lead status.
    /// </summary>
    private static readonly Dictionary<string, string> EventToStatus = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SessionStart"] = "start",
        ["UserPromptSubmit"] = "busy",
        ["PreToolUse"] = "busy",
        ["PreCompact"] = "compact",
        ["PostCompact"] = "idle",
        ["Stop"] = "idle",
        ["StopFailure"] = "error",
        ["Notification"] = "attention",
        ["PermissionRequest"] = "permission",
        ["PostToolUse"] = "busy",
        ["PostToolUseFailure"] = "error",
        ["SubagentStart"] = "busy",
        ["SubagentStop"] = "busy",
        ["Elicitation"] = "permission",
        ["WorktreeCreate"] = "busy",
        ["TaskCreated"] = "busy",
        ["TaskCompleted"] = "busy",
        ["TeammateIdle"] = "busy",
        ["PermissionDenied"] = "idle",
        ["SessionEnd"] = "end",
    };

    /// <summary>
    /// Derives a status string from a hook event name and optional context.
    /// </summary>
    /// <param name="eventName">The Claude Code hook event name.</param>
    /// <param name="source">Optional source field (e.g., "resume" for SessionStart).</param>
    /// <param name="notificationType">Optional notification type (e.g., "permission_prompt").</param>
    /// <returns>The derived status string, or "unknown" if the event is not recognized.</returns>
    public static string DeriveStatus(string eventName, string? source = null, string? notificationType = null)
    {
        // Special case: SessionStart with resume source → idle (returning to existing session)
        if (string.Equals(eventName, "SessionStart", StringComparison.OrdinalIgnoreCase)
            && string.Equals(source, "resume", StringComparison.OrdinalIgnoreCase))
        {
            return "idle";
        }

        // Special case: Notification with permission_prompt → permission
        if (string.Equals(eventName, "Notification", StringComparison.OrdinalIgnoreCase)
            && string.Equals(notificationType, "permission_prompt", StringComparison.OrdinalIgnoreCase))
        {
            return "permission";
        }

        // Special case: Notification with idle_prompt → idle (authoritative "genuinely waiting for user" signal)
        if (string.Equals(eventName, "Notification", StringComparison.OrdinalIgnoreCase)
            && string.Equals(notificationType, "idle_prompt", StringComparison.OrdinalIgnoreCase))
        {
            return "idle";
        }

        return EventToStatus.TryGetValue(eventName, out var status) ? status : "unknown";
    }
}
