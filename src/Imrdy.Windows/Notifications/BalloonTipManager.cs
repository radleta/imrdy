using Imrdy.Windows.Models;
using Microsoft.Extensions.Logging;

namespace Imrdy.Windows.Notifications;

/// <summary>
/// Shows balloon tip notifications on status transitions.
/// Implements same-desktop suppression and bootstrap suppression.
/// </summary>
internal sealed class BalloonTipManager
{
    private readonly ILogger _logger;

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

        // Only notify on transitions to attention states
        if (newStatus is not ("attention" or "permission"))
        {
            return;
        }

        // Same-desktop suppression: don't toast if session is on current desktop
        if (currentDesktopIndex.HasValue && entry.DesktopIndex == currentDesktopIndex)
        {
            return;
        }

        if (entry.Icon is null)
        {
            return;
        }

        var title = entry.State.Project;
        var text = newStatus == "permission"
            ? "Permission request"
            : "Needs your attention";

        if (!string.IsNullOrEmpty(entry.State.SessionName))
        {
            title = $"{entry.State.Project}: {entry.State.SessionName}";
        }

        try
        {
            entry.Icon.BalloonTipClicked += (_, _) =>
            {
                entry.LastSeenAt = DateTimeOffset.UtcNow;
                _logger.LogDebug("Balloon tip clicked for {SessionId}", entry.SessionId);
            };

            entry.Icon.ShowBalloonTip(5000, title, text, ToolTipIcon.Info);
            _logger.LogDebug("Balloon tip shown for {SessionId}: {Text}", entry.SessionId, text);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to show balloon tip for {SessionId}", entry.SessionId);
        }
    }
}
