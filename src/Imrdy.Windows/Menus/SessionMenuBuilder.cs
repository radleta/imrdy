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
                Rebuild(menu, entry, onDismiss);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Error rebuilding session menu for {SessionId}", entry.SessionId);
            }
        };
        return menu;
    }

    private static void Rebuild(
        ContextMenuStrip menu,
        SessionEntry entry,
        Action onDismiss)
    {
        menu.Items.Clear();

        // Header: project name + status
        var header = new ToolStripMenuItem($"{entry.State.Project} [{entry.State.Status}]")
        {
            Enabled = false,
        };
        menu.Items.Add(header);
        menu.Items.Add(new ToolStripSeparator());

        // Manage submenu
        var manage = new ToolStripMenuItem("Manage");
        manage.DropDownItems.Add("Dismiss", null, (_, _) => onDismiss());
        menu.Items.Add(manage);

    }
}
