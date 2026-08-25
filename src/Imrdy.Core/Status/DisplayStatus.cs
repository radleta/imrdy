using Imrdy.Core.Hooks;

namespace Imrdy.Core.Status;

/// <summary>
/// Resolves the status stored on a state file (lead readiness) into the status shown to the user.
/// <para>
/// The state file records one thing: whether the <b>main session</b> is waiting for the user. That
/// is the right basis for notifications, but on its own it overloads green — a lead that has
/// returned control while background work keeps running is waiting, yet it may resume itself
/// the moment an agent reports back, without the user doing anything.
/// </para>
/// <para>
/// So an idle lead whose stored roster still lists running work displays as "done" (teal): waiting,
/// but not free. Green then means what it should — nothing is running and the session is yours.
/// Teal is silent (not in <c>DefaultToastEvents</c>); the toast and Finished sound fire on the
/// teal → green transition, once the roster comes back empty.
/// </para>
/// <para>
/// The mechanism is a roster snapshot, not a time window. <c>runningTasks</c> is the
/// <c>background_tasks</c> list Claude Code reported on the most recent roster-bearing hook event,
/// and the whole design rests on one invariant: <i>whenever the lead is "idle", the stored roster
/// describes work owned by the currently-running Claude Code process</i>. <c>Stop</c> carries a
/// fresh roster and lands it in the same atomic state-file write as the status it establishes.
/// Four other events also produce "idle" without carrying one — <c>PostCompact</c>,
/// <c>PermissionDenied</c>, <c>SessionStart</c> with <c>source</c> "resume", and
/// <c>Notification</c> with <c>notification_type</c> "idle_prompt". Three of those stay inside the
/// same process, so the preserved roster is still true; <c>SessionStart</c> is a process boundary
/// and clears the roster instead of preserving it. That closure is why no expiry, timer, or
/// cleanup policy appears anywhere in this type.
/// </para>
/// </summary>
public static class DisplayStatus
{
    /// <summary>
    /// Maps a stored lead status to the status to display.
    /// Only "idle" is rewritten, and only when <paramref name="runningTasks"/> is non-empty — every
    /// other status already describes the lead accurately and passes through untouched.
    /// </summary>
    /// <param name="status">The stored lead status, compared ordinal-ignore-case.</param>
    /// <param name="runningTasks">
    /// The stored roster. <c>null</c> means no measurement is known and the session shows green —
    /// the degradation path for a Claude Code build that stops sending <c>background_tasks</c>.
    /// An empty list means work was measured and none of it is still running. Any non-empty list
    /// means teal, counting <b>every</b> entry regardless of its
    /// <see cref="BackgroundTaskModel.Status"/> value: filtering on a vocabulary with exactly one
    /// observed member would fail silently the day that vocabulary changed.
    /// </param>
    public static string Resolve(string status, IReadOnlyList<BackgroundTaskModel>? runningTasks)
        => string.Equals(status, "idle", StringComparison.OrdinalIgnoreCase) && runningTasks is { Count: > 0 }
            ? "done"
            : status;

    /// <summary>
    /// True when the session is waiting for the user but background work is still running,
    /// so it may resume itself without user input.
    /// </summary>
    public static bool IsIdleWithAgentsRunning(string status, IReadOnlyList<BackgroundTaskModel>? runningTasks)
        => string.Equals(status, "idle", StringComparison.OrdinalIgnoreCase)
            && Resolve(status, runningTasks) == "done";
}
