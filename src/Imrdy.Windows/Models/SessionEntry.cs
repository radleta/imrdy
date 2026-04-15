using Imrdy.Core.State;

namespace Imrdy.Windows.Models;

/// <summary>
/// In-memory model tracking a session's state, icon, and lifecycle.
/// </summary>
internal sealed class SessionEntry : IDisposable
{
    public required string SessionId { get; init; }
    public StateFileModel State { get; set; } = null!;
    public NotifyIcon? Icon { get; set; }
    public ContextMenuStrip? Menu { get; set; }

    /// <summary>Last time the user interacted with this session (click/focus). Used for aging.</summary>
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When the current status was set. Used for tooltip age display.</summary>
    public DateTimeOffset StatusSince { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Time after which this session should be removed (grace period on SessionEnd).</summary>
    public DateTimeOffset? RemoveAfter { get; set; }

    /// <summary>Whether the user has dismissed this session via context menu.</summary>
    public bool Dismissed { get; set; }

    /// <summary>Assigned sound pack name.</summary>
    public string? SoundPack { get; set; }

    /// <summary>Assigned icon style (shape name or pack style). Null means use the global default.</summary>
    public string? IconStyle { get; set; }

    /// <summary>Assigned virtual desktop index.</summary>
    public int? DesktopIndex { get; set; }

    /// <summary>Last computed aging tier. Used to avoid unnecessary icon updates.</summary>
    public int LastAgingTier { get; set; } = 0;

    /// <summary>True after consensus promotion has been triggered for the current "done" status.
    /// Reset when status changes away from "done".</summary>
    public bool ConsensusPromoted { get; set; }

    public void Dispose()
    {
        Icon?.Visible = false;
        Icon?.Dispose();
        Icon = null;
        Menu?.Dispose();
        Menu = null;
    }
}
