namespace Imrdy.Core.Notifications;

/// <summary>
/// Pure helper for consensus promotion eligibility. Encodes the state matrix:
///
/// | LastTeammateAt        | StatusSince &lt; 90s                            | StatusSince &gt;= 90s                  |
/// |-----------------------|------------------------------------------------|---------------------------------------|
/// | null                  | false (early return — solo path handles via dwell) | false (early return — solo path)  |
/// | &lt; 15s (active)        | false (suppressed — both gates fail)           | true (bypass fires: session stalled)  |
/// | &gt;= 15s (quiet)        | true (existing behavior — quiet path fires)    | true (redundant — quiet already fires)|
///
/// The bold cell (active teammates, aged status) is the bug fix: consensus was permanently
/// suppressed when teammates pulsed faster than TeammateQuietThreshold. The MaxDoneTime
/// bypass fires regardless of teammate cadence once the session has sat at "done" long enough.
/// </summary>
public static class ConsensusGate
{
    /// <summary>
    /// Returns true when a session in status "done" is eligible for consensus promotion to "idle".
    /// Two independent OR-conditions: teammate silence for <paramref name="quietThreshold"/>,
    /// OR time at "done" status exceeding <paramref name="maxDoneTime"/>.
    /// </summary>
    /// <param name="lastTeammateAt">Last time a teammate event was received; null for solo sessions.</param>
    /// <param name="statusSince">When the session last transitioned into "done".</param>
    /// <param name="now">Current wall-clock time (caller's snapshot).</param>
    /// <param name="quietThreshold">Time after which teammates are considered quiet (TeammateQuietThreshold).</param>
    /// <param name="maxDoneTime">Maximum time at "done" before bypass fires (MaxDoneTime).</param>
    public static bool IsEligibleForPromotion(
        DateTimeOffset? lastTeammateAt,
        DateTimeOffset statusSince,
        DateTimeOffset now,
        TimeSpan quietThreshold,
        TimeSpan maxDoneTime)
    {
        // Solo sessions (no teammates ever) are not eligible — drain loop's own
        // null-guard at TrayApp.cs:418 already routes them to the normal dwell
        // path. This guard is intentionally redundant: it makes the helper
        // self-contained for unit testing without requiring callers to pre-filter.
        if (lastTeammateAt is null) return false;

        var sinceTeammate = now - lastTeammateAt.Value;
        var sinceStatus = now - statusSince;
        return sinceTeammate >= quietThreshold || sinceStatus >= maxDoneTime;
    }
}
