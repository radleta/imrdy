namespace Imrdy.Core.Sound;

/// <summary>
/// Tracks per-session sound cooldowns and detects combo events
/// when multiple distinct sessions fire sounds within a time window.
/// </summary>
public sealed class CooldownTracker
{
    private readonly TimeSpan _cooldownDuration;
    private readonly TimeSpan _comboWindow;
    private readonly Dictionary<string, DateTimeOffset> _lastPlayed = new();
    private readonly List<(string SessionId, DateTimeOffset Time)> _recentPlays = new();

    public CooldownTracker(TimeSpan? cooldownDuration = null, TimeSpan? comboWindow = null)
    {
        _cooldownDuration = cooldownDuration ?? TimeSpan.FromSeconds(5);
        _comboWindow = comboWindow ?? TimeSpan.FromSeconds(3);
    }

    /// <summary>
    /// Checks if a session is allowed to play a sound (not in cooldown).
    /// </summary>
    public bool IsOnCooldown(string sessionId, DateTimeOffset now)
    {
        if (_lastPlayed.TryGetValue(sessionId, out var lastTime))
        {
            return now - lastTime < _cooldownDuration;
        }

        return false;
    }

    /// <summary>
    /// Records that a session played a sound and checks for combo.
    /// Returns true if this triggers a combo event (2+ distinct sessions within the combo window).
    /// Combo fires once per burst — the window resets after a combo is detected.
    /// </summary>
    public bool RecordAndCheckCombo(string sessionId, DateTimeOffset now)
    {
        _lastPlayed[sessionId] = now;

        // Clean up old entries outside the combo window
        _recentPlays.RemoveAll(e => now - e.Time > _comboWindow);

        _recentPlays.Add((sessionId, now));

        // Combo: 2+ distinct sessions within the window
        var distinctSessions = _recentPlays.Select(e => e.SessionId).Distinct().Count();
        if (distinctSessions >= 2)
        {
            // Reset after firing — combo fires once per burst
            _recentPlays.Clear();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Removes a session from cooldown tracking (e.g., on session end).
    /// </summary>
    public void RemoveSession(string sessionId)
    {
        _lastPlayed.Remove(sessionId);
    }

    /// <summary>
    /// Clears all tracking state.
    /// </summary>
    public void Clear()
    {
        _lastPlayed.Clear();
        _recentPlays.Clear();
    }
}
