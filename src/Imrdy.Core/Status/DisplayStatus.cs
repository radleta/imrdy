namespace Imrdy.Core.Status;

/// <summary>
/// Resolves the status stored on a state file (lead readiness) into the status shown to the user.
/// <para>
/// The state file records one thing: whether the <b>main session</b> is waiting for the user. That
/// is the right basis for notifications, but on its own it overloads green — a lead that has
/// returned control while background subagents keep working is waiting, yet it may resume itself
/// the moment an agent reports back, without the user doing anything.
/// </para>
/// <para>
/// So an idle lead with recent subagent activity displays as "done" (teal): waiting, but not free.
/// Green then means what it should — nothing is running and the session is yours. Teal is silent
/// (not in <c>DefaultToastEvents</c>); the toast and Finished sound fire on the teal → green
/// transition, once subagents have actually fallen quiet.
/// </para>
/// </summary>
public static class DisplayStatus
{
    /// <summary>
    /// How long after the last subagent event a session still counts as having agents running.
    /// <para>
    /// Chosen by measurement, not intuition: over 1085 consecutive-event gaps from 45 real agents,
    /// p50 = 7.5s, p90 = 23s, p95 = 38s, p99 = 75s, max = 153s. A working agent can therefore go
    /// quiet for over a minute between tool calls. Shorter windows declare "agents finished"
    /// while they are merely thinking — a 15s threshold would have been wrong 19.6% of the time,
    /// 30s wrong 6.9%, 60s wrong 1.7%. At 2 minutes exactly one gap in 1085 (0.09%) overruns.
    /// </para>
    /// <para>
    /// The asymmetry justifies erring long: a premature flip to green costs a false "session is
    /// free" toast — the exact noise this feature exists to remove — while overrunning only
    /// delays good news on a surface the user reads peripherally.
    /// </para>
    /// </summary>
    public static readonly TimeSpan TeammatePresenceTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Maps a stored lead status to the status to display.
    /// Only "idle" is rewritten, and only while subagent activity is fresh — every other status
    /// already describes the lead accurately and passes through untouched.
    /// </summary>
    public static string Resolve(string status, DateTimeOffset? lastTeammateAt, DateTimeOffset now)
    {
        if (!string.Equals(status, "idle", StringComparison.OrdinalIgnoreCase))
        {
            return status;
        }

        if (lastTeammateAt is null)
        {
            return status;
        }

        return now - lastTeammateAt.Value < TeammatePresenceTimeout ? "done" : status;
    }

    /// <summary>
    /// True when the session is waiting for the user but subagents are still running,
    /// so it may resume itself without user input.
    /// </summary>
    public static bool IsIdleWithAgentsRunning(string status, DateTimeOffset? lastTeammateAt, DateTimeOffset now)
        => string.Equals(status, "idle", StringComparison.OrdinalIgnoreCase)
            && Resolve(status, lastTeammateAt, now) == "done";
}
