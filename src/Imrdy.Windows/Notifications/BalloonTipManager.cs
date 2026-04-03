using Imrdy.Windows.Models;
using Microsoft.Extensions.Logging;

namespace Imrdy.Windows.Notifications;

/// <summary>
/// Shows balloon tip notifications on status transitions.
/// Implements same-desktop suppression and bootstrap suppression.
/// Click handler is wired at icon creation time in TrayApp (not here).
/// </summary>
internal sealed class BalloonTipManager
{
    private static readonly HashSet<string> DefaultToastEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        "idle", "attention", "permission",
    };

    private readonly ILogger _logger;

    /// <summary>
    /// When true, all balloon tips are suppressed (--no-toast flag).
    /// </summary>
    public bool Disabled { get; set; }

    /// <summary>
    /// When true, balloon tips use ToolTipIcon.None to suppress the Windows notification
    /// sound (sound packs provide their own audio). Matches PS1 reference behavior.
    /// </summary>
    public bool SuppressSystemSound { get; set; }

    public BalloonTipManager(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<BalloonTipManager>();
    }

    /// <summary>
    /// Shows a balloon tip for a session status transition if appropriate.
    /// Suppressed during bootstrap and when the session is on the current desktop.
    /// </summary>
    public void OnStatusTransition(
        SessionEntry entry,
        string previousStatus,
        string newStatus,
        bool isBootstrapping,
        int? currentDesktopIndex)
    {
        if (isBootstrapping)
        {
            return;
        }

        // Only notify on configured toast events
        if (!DefaultToastEvents.Contains(newStatus))
        {
            return;
        }

        // Same-desktop suppression: don't toast if session is on current desktop
        if (currentDesktopIndex.HasValue && entry.DesktopIndex == currentDesktopIndex)
        {
            return;
        }

        ShowBalloon(entry, newStatus);
    }

    /// <summary>
    /// Shows a balloon tip for a newly created session.
    /// </summary>
    public void OnNewSession(
        SessionEntry entry,
        bool isBootstrapping)
    {
        if (isBootstrapping)
        {
            return;
        }

        ShowBalloon(entry, entry.State.Status);
    }

    private void ShowBalloon(SessionEntry entry, string status)
    {
        if (Disabled || entry.Icon is null)
        {
            return;
        }

        var title = entry.State.Project;
        if (!string.IsNullOrEmpty(entry.State.SessionName))
        {
            title = $"{entry.State.Project}: {entry.State.SessionName}";
        }

        // Toast body: use last_message if available, otherwise status-based text
        var text = status switch
        {
            "permission" => "Permission request",
            "attention" => "Needs your attention",
            "idle" => "Finished",
            _ => $"Status: {status}",
        };

        if (!string.IsNullOrEmpty(entry.State.LastMessage))
        {
            text = entry.State.LastMessage;
        }

        try
        {
            var icon = SuppressSystemSound ? ToolTipIcon.None : ToolTipIcon.Info;
            entry.Icon.ShowBalloonTip(5000, title, text, icon);
            _logger.LogInformation("Balloon tip shown for {SessionId}: {Title} — {Text}", entry.SessionId, title, text);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to show balloon tip for {SessionId}", entry.SessionId);
        }
    }
}
