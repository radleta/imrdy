using System.Diagnostics;

namespace Imrdy.Core.Sound;

/// <summary>
/// A notification that has dwelled long enough and passed the per-session toast cooldown check.
/// </summary>
public readonly record struct FiredNotification(string SessionId, string Status, string PreviousStatus, string? NotificationType);

/// <summary>
/// Tracks per-session notification dwell state, gating toast and sound notifications
/// behind per-status dwell durations and a per-session toast cooldown.
///
/// Must be called from the UI thread only — not thread-safe.
/// </summary>
public sealed class NotificationDwellState
{
    [DebuggerDisplay("{Status} pending={IsPending}")]
    private sealed class DwellEntry
    {
        public string Status { get; set; } = string.Empty;
        public string PreviousStatus { get; set; } = string.Empty;
        public string? NotificationType { get; set; }
        public DateTimeOffset ChangedAt { get; set; }
        public DateTimeOffset? LastNotifiedAt { get; set; }
        public bool IsPending { get; set; }
    }

    private static readonly Dictionary<string, TimeSpan> DwellDurations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["idle"]       = TimeSpan.FromSeconds(5),
        ["compact"]    = TimeSpan.FromSeconds(5),
        ["busy"]       = TimeSpan.FromSeconds(3),
        ["error"]      = TimeSpan.FromSeconds(3),
        ["permission"] = TimeSpan.FromSeconds(3),
        ["attention"]  = TimeSpan.FromSeconds(3),
        ["end"]        = TimeSpan.FromSeconds(2),
    };

    private static readonly TimeSpan ToastCooldown = TimeSpan.FromSeconds(10);

    private readonly Dictionary<string, DwellEntry> _sessions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Records a status change for a session, replacing any pending notification (latest wins).
    /// <see cref="DwellEntry.LastNotifiedAt"/> is intentionally preserved from an existing entry
    /// to maintain the 10-second toast cooldown across rapid replacements.
    /// </summary>
    public void OnStatusChanged(string sessionId, string status, string previousStatus, DateTimeOffset now, string? notificationType = null)
    {
        if (_sessions.TryGetValue(sessionId, out var existing))
        {
            existing.Status = status;
            existing.PreviousStatus = previousStatus;
            existing.NotificationType = notificationType;
            existing.ChangedAt = now;
            // LastNotifiedAt intentionally preserved — do not reset toast cooldown on rapid replacements
            existing.IsPending = true;
        }
        else
        {
            _sessions[sessionId] = new DwellEntry
            {
                Status = status,
                PreviousStatus = previousStatus,
                NotificationType = notificationType,
                ChangedAt = now,
                LastNotifiedAt = null,
                IsPending = true,
            };
        }
    }

    private static TimeSpan GetDwellDuration(string status)
        => DwellDurations.TryGetValue(status, out var duration) ? duration : TimeSpan.FromSeconds(3);

    /// <summary>
    /// Returns all sessions whose pending notification has satisfied both the dwell duration
    /// and the per-session toast cooldown. Marks fired entries as no longer pending (they remain
    /// in the dictionary for cooldown tracking until the next <see cref="OnStatusChanged"/> call).
    ///
    /// Non-throwing by contract — returns an empty list if internal state is inconsistent.
    /// </summary>
    public IReadOnlyList<FiredNotification> GetFiredSessions(DateTimeOffset now)
    {
        try
        {
            List<FiredNotification>? result = null;

            foreach (var (sessionId, entry) in _sessions)
            {
                if (!entry.IsPending)
                    continue;

                if (now - entry.ChangedAt < GetDwellDuration(entry.Status))
                    continue;

                if (entry.LastNotifiedAt is not null && now - entry.LastNotifiedAt < ToastCooldown)
                    continue;

                entry.IsPending = false;
                entry.LastNotifiedAt = now;

                result ??= new List<FiredNotification>();
                result.Add(new FiredNotification(sessionId, entry.Status, entry.PreviousStatus, entry.NotificationType));
            }

            return (IReadOnlyList<FiredNotification>?)result ?? Array.Empty<FiredNotification>();
        }
        catch
        {
            return Array.Empty<FiredNotification>();
        }
    }

    /// <summary>
    /// Removes a session from dwell tracking (e.g., on session end).
    /// </summary>
    public void RemoveSession(string sessionId) => _sessions.Remove(sessionId);

    /// <summary>
    /// Clears all dwell tracking state.
    /// </summary>
    public void Clear() => _sessions.Clear();
}
