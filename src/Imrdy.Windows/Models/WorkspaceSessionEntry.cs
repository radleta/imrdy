using Imrdy.Core.Workspace;

namespace Imrdy.Windows.Models;

/// <summary>
/// In-memory model tracking a workspace's icon, visibility, and lifecycle.
/// </summary>
internal sealed class WorkspaceSessionEntry : IDisposable
{
    public required WorkspaceEntry Workspace { get; set; }
    public NotifyIcon? Icon { get; set; }
    public ContextMenuStrip? Menu { get; set; }

    /// <summary>Last time the user interacted with this workspace (click/focus).</summary>
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Whether the white dot is currently visible (no active sessions).</summary>
    public bool Visible { get; set; } = true;

    /// <summary>Per-workspace icon style override. Null means use global config.</summary>
    public string? IconStyle { get; set; }

    public void Dispose()
    {
        Icon?.Visible = false;
        Icon?.Dispose();
        Icon = null;
        Menu?.Dispose();
        Menu = null;
    }
}
