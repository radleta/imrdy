using Imrdy.Core.Menus;
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
                var state = new WorkspaceMenuState
                {
                    WorkspaceName = entry.Workspace.Name,
                    WorkspacePath = entry.Workspace.Path,
                };
                var items = WorkspaceMenuModel.Build(state);
                MenuRenderer.Apply(menu, items, tag => OnClick(tag, onUnpin), logger);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Error rebuilding workspace menu for {Name}", entry.Workspace.Name);
            }
        };
        return menu;
    }

    private static void OnClick(string tag, Action<string> onUnpin)
    {
        if (tag.StartsWith("unpin:", StringComparison.Ordinal))
        {
            onUnpin(tag["unpin:".Length..]);
        }
    }
}
