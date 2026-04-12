using Imrdy.Core;
using Imrdy.Windows.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.Uwp.Notifications;

namespace Imrdy.Windows.Notifications;

/// <summary>
/// Shows Windows toast notifications on status transitions.
/// Uses the Windows.UI.Notifications toast API for reliable click handling.
/// Click handler is registered globally via ToastNotificationManagerCompat.
/// </summary>
internal sealed class BalloonTipManager : IDisposable
{
    private static readonly HashSet<string> DefaultToastEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        "idle", "attention", "permission", "error",
    };

    private readonly ILogger _logger;
    private readonly string? _iconPath;

    /// <summary>
    /// When true, all toasts are suppressed (--no-toast flag).
    /// </summary>
    public bool Disabled { get; set; }

    /// <summary>
    /// When true, toasts use the silent audio attribute to suppress the Windows notification
    /// sound (sound packs provide their own audio). Matches PS1 reference behavior.
    /// </summary>
    public bool SuppressSystemSound { get; set; }

    /// <summary>
    /// Callback invoked on the UI thread when the user clicks a toast notification.
    /// The string argument is the session ID.
    /// </summary>
    public Action<string>? OnToastClicked { get; set; }

    public BalloonTipManager(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<BalloonTipManager>();
        _iconPath = ExtractIcon();

        ToastNotificationManagerCompat.OnActivated += OnToastActivated;
    }

    /// <summary>
    /// Extracts the embedded imrdy.ico as a PNG to ~/.imrdy/ so toast notifications can reference it.
    /// Toast API requires PNG format for app logo override.
    /// </summary>
    private string? ExtractIcon()
    {
        try
        {
            var path = Path.Combine(ImrdyPaths.Home, "imrdy.png");
            using var stream = typeof(BalloonTipManager).Assembly
                .GetManifestResourceStream("Imrdy.Windows.Resources.imrdy.ico");
            if (stream is null) return null;

            using var icon = new Icon(stream, 64, 64);
            using var bitmap = icon.ToBitmap();
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            return path;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract icon for toast notifications");
            return null;
        }
    }

    /// <summary>
    /// Shows a toast for a session status transition if appropriate.
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

        ShowToast(entry, newStatus);
    }

    /// <summary>
    /// Shows a toast for a newly created session.
    /// </summary>
    public void OnNewSession(
        SessionEntry entry,
        bool isBootstrapping)
    {
        if (isBootstrapping)
        {
            return;
        }

        ShowToast(entry, entry.State.Status);
    }

    private void ShowToast(SessionEntry entry, string status)
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
            "error" => "Tool failure",
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
            var builder = new ToastContentBuilder()
                .AddArgument("sessionId", entry.SessionId)
                .AddText(title)
                .AddText(text);

            if (_iconPath is not null)
            {
                builder.AddAppLogoOverride(new Uri(_iconPath), ToastGenericAppLogoCrop.Circle);
            }

            if (SuppressSystemSound)
            {
                builder.AddAudio(null, null, true);  // silent
            }

            builder.Show();

            _logger.LogInformation("Toast shown for {SessionId}: {Title} — {Text}",
                entry.SessionId, title, text);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to show toast for {SessionId}", entry.SessionId);
        }
    }

    private void OnToastActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        var args = ToastArguments.Parse(e.Argument);
        if (args.TryGetValue("sessionId", out var sessionId))
        {
            _logger.LogInformation("Toast CLICKED for {SessionId}", sessionId);
            OnToastClicked?.Invoke(sessionId);
        }
    }

    public void Dispose()
    {
        ToastNotificationManagerCompat.OnActivated -= OnToastActivated;
    }
}
