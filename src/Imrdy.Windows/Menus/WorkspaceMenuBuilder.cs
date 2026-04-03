using Imrdy.Core.Workspace;
using Imrdy.Windows.Models;
using Microsoft.Extensions.Logging;

namespace Imrdy.Windows.Menus;

/// <summary>
/// Builds context menus for workspace tray icons.
/// </summary>
internal static class WorkspaceMenuBuilder
{
    /// <summary>
    /// Creates a context menu for a workspace entry.
    /// </summary>
    public static ContextMenuStrip Create(
        WorkspaceSessionEntry entry,
        Action<string> onUnpin,
        ILogger? logger = null)
    {
        var menu = new ContextMenuStrip();
        menu.Opening += (_, _) =>
        {
            try
            {
                Rebuild(menu, entry, onUnpin);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Error rebuilding workspace menu for {Name}", entry.Workspace.Name);
            }
        };
        return menu;
    }

    private static void Rebuild(
        ContextMenuStrip menu,
        WorkspaceSessionEntry entry,
        Action<string> onUnpin)
    {
        menu.Items.Clear();

        // Header: workspace name
        var header = new ToolStripMenuItem($"{entry.Workspace.Name} [workspace]")
        {
            Enabled = false,
        };
        menu.Items.Add(header);
        menu.Items.Add(new ToolStripSeparator());

        // Manage submenu
        var manage = new ToolStripMenuItem("Manage");
        manage.DropDownItems.Add("Unpin", null, (_, _) => onUnpin(entry.Workspace.Path));
        menu.Items.Add(manage);

    }
}
