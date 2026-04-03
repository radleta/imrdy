using Imrdy.Core.Menus;
using Imrdy.Windows.Models;
using Microsoft.Extensions.Logging;

namespace Imrdy.Windows.Menus;

/// <summary>
/// Builds context menus for session tray icons.
/// Uses Opening event for dynamic content (desktop list, sound pack list).
/// </summary>
internal static class SessionMenuBuilder
{
    /// <summary>
    /// Creates a context menu for a session entry.
    /// </summary>
    public static ContextMenuStrip Create(
        SessionEntry entry,
        Action onDismiss,
        ILogger? logger = null)
    {
        var menu = new ContextMenuStrip();
        menu.Opening += (_, _) =>
        {
            try
            {
                var state = new SessionMenuState
                {
                    SessionId = entry.SessionId,
                    Status = entry.State.Status,
                    Project = entry.State.Project,
                };
                var items = SessionMenuModel.Build(state);
                MenuRenderer.Apply(menu, items, tag => OnClick(tag, onDismiss), logger);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Error rebuilding session menu for {SessionId}", entry.SessionId);
            }
        };
        return menu;
    }

    private static void OnClick(string tag, Action onDismiss)
    {
        if (tag == "dismiss")
        {
            onDismiss();
        }
    }
}
