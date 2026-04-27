using Imrdy.Core.Display;

namespace Imrdy.Core.Hooks;

/// <summary>
/// Per-session ring buffers and counters for hook-derived dashboard signals.
/// Pure managed state — no I/O, no platform dependencies. GC'd with the owning TrayApp.
/// </summary>
/// <remarks>
/// All public methods are gated by a single lock so callers on different threads
/// (FSW callbacks on the UI thread, background aggregation) see a consistent view.
/// Callers pass <c>derivedStatus</c> to <see cref="Apply"/> so the store doesn't
/// duplicate <c>StatusDerivation</c> logic — matches the spec at idea.md D10.
/// </remarks>
public sealed class HookAccumulationStore
{
    private const int RecentToolsCap = 8;
    private static readonly TimeSpan ActivityWindow = TimeSpan.FromSeconds(60);

    private readonly Dictionary<string, SessionState> _sessions = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <summary>
    /// Test seam. Tests may override this to inject a deterministic clock so
    /// pruning and ring-buffer semantics can be verified without wall-clock timing.
    /// Production callers never set this.
    /// </summary>
    internal Func<DateTimeOffset> NowProvider { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>
    /// Applies a hook event to the per-session accumulators.
    /// <paramref name="derivedStatus"/> is the already-computed status the caller holds
    /// (typically <c>entry.State.Status</c>) — passed in to avoid re-deriving inside the store.
    /// Prunes <see cref="HookAccumulation.ActivityTimestamps"/> older than 60 seconds on every call.
    /// </summary>
    public void Apply(HookEventModel evt, string derivedStatus)
    {
        ArgumentNullException.ThrowIfNull(evt);
        ArgumentNullException.ThrowIfNull(derivedStatus);

        if (string.IsNullOrEmpty(evt.SessionId))
        {
            return;
        }

        var now = NowProvider();

        lock (_gate)
        {
            if (!_sessions.TryGetValue(evt.SessionId, out var session))
            {
                session = new SessionState();
                _sessions[evt.SessionId] = session;
            }

            ApplyEvent(session, evt, derivedStatus, now);
            PruneActivity(session, now);
            session.LastStatus = derivedStatus;
        }
    }

    /// <summary>
    /// Returns a snapshot of the named session's accumulators.
    /// Returns an empty snapshot when the session is unknown.
    /// </summary>
    public HookAccumulation GetSnapshot(string sessionId)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                return Empty();
            }

            return Snapshot(session);
        }
    }

    /// <summary>
    /// Returns snapshots for every known session — used by fleet-strip aggregation.
    /// </summary>
    public IReadOnlyDictionary<string, HookAccumulation> SnapshotAll()
    {
        lock (_gate)
        {
            var result = new Dictionary<string, HookAccumulation>(_sessions.Count, StringComparer.Ordinal);
            foreach (var (id, session) in _sessions)
            {
                result[id] = Snapshot(session);
            }
            return result;
        }
    }

    private static void ApplyEvent(SessionState session, HookEventModel evt, string derivedStatus, DateTimeOffset now)
    {
        var hookEvent = evt.HookEventName;

        if (string.Equals(hookEvent, "SessionStart", StringComparison.Ordinal))
        {
            session.TurnCount = 0;
            session.FailureCount = 0;
            session.RecentTools.Clear();
            session.ActivityTimestamps.Clear();
            session.ActiveAgentIds.Clear();
            session.CurrentTool = null;
            session.PermissionTool = null;
            return;
        }

        if (string.Equals(hookEvent, "UserPromptSubmit", StringComparison.Ordinal))
        {
            session.ActiveAgentIds.Clear();
            session.TurnCount++;
        }
        else if (string.Equals(hookEvent, "PostToolUseFailure", StringComparison.Ordinal)
                 || string.Equals(hookEvent, "PermissionDenied", StringComparison.Ordinal))
        {
            session.FailureCount++;
        }

        if (string.Equals(hookEvent, "PostToolUse", StringComparison.Ordinal)
            && !string.IsNullOrEmpty(evt.ToolName))
        {
            session.RecentTools.Add(new RecentToolEntry(evt.ToolName, now));
            while (session.RecentTools.Count > RecentToolsCap)
            {
                session.RecentTools.RemoveAt(0);
            }
        }

        if (!string.IsNullOrEmpty(evt.AgentId))
        {
            session.ActiveAgentIds.Add(evt.AgentId);
        }

        // Every non-SessionStart hook event contributes to the 60s activity density —
        // matches idea.md's "hook-timestamp density over last 60s" language for the sparkline.
        session.ActivityTimestamps.Add(now);

        ApplyStatusTransition(session, derivedStatus, evt.ToolName);
    }

    private static void ApplyStatusTransition(SessionState session, string derivedStatus, string? toolName)
    {
        var prior = session.LastStatus;

        var enteringBusy = !string.Equals(prior, "busy", StringComparison.Ordinal)
                           && string.Equals(derivedStatus, "busy", StringComparison.Ordinal);
        var leavingBusy = string.Equals(prior, "busy", StringComparison.Ordinal)
                          && !string.Equals(derivedStatus, "busy", StringComparison.Ordinal);

        if (enteringBusy)
        {
            session.CurrentTool = toolName;
        }
        else if (leavingBusy)
        {
            session.CurrentTool = null;
        }

        var enteringPermission = !string.Equals(prior, "permission", StringComparison.Ordinal)
                                 && string.Equals(derivedStatus, "permission", StringComparison.Ordinal);
        var leavingPermission = string.Equals(prior, "permission", StringComparison.Ordinal)
                                && !string.Equals(derivedStatus, "permission", StringComparison.Ordinal);

        if (enteringPermission)
        {
            session.PermissionTool = toolName;
        }
        else if (leavingPermission)
        {
            session.PermissionTool = null;
        }
    }

    private static void PruneActivity(SessionState session, DateTimeOffset now)
    {
        var cutoff = now - ActivityWindow;
        while (session.ActivityTimestamps.Count > 0 && session.ActivityTimestamps[0] < cutoff)
        {
            session.ActivityTimestamps.RemoveAt(0);
        }
    }

    private static HookAccumulation Snapshot(SessionState session)
    {
        return new HookAccumulation(
            TurnCount: session.TurnCount,
            FailureCount: session.FailureCount,
            RecentTools: session.RecentTools.ToArray(),
            ActivityTimestamps: session.ActivityTimestamps.ToArray(),
            ActiveAgentIds: new HashSet<string>(session.ActiveAgentIds, StringComparer.Ordinal),
            CurrentTool: session.CurrentTool,
            PermissionTool: session.PermissionTool);
    }

    private static HookAccumulation Empty()
    {
        return new HookAccumulation(
            TurnCount: 0,
            FailureCount: 0,
            RecentTools: Array.Empty<RecentToolEntry>(),
            ActivityTimestamps: Array.Empty<DateTimeOffset>(),
            ActiveAgentIds: new HashSet<string>(StringComparer.Ordinal),
            CurrentTool: null,
            PermissionTool: null);
    }

    private sealed class SessionState
    {
        public int TurnCount { get; set; }
        public int FailureCount { get; set; }
        public List<RecentToolEntry> RecentTools { get; } = new();
        public List<DateTimeOffset> ActivityTimestamps { get; } = new();
        public HashSet<string> ActiveAgentIds { get; } = new(StringComparer.Ordinal);
        public string? CurrentTool { get; set; }
        public string? PermissionTool { get; set; }
        public string? LastStatus { get; set; }
    }
}
