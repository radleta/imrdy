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
        Action onAssignDesktop,
        Action<int> onSetDesktop,
        Func<int?> getDesktopCount,
        Func<bool> getDesktopAvailable,
        Action<string?> onSetIconStyle,
        Func<IReadOnlyList<string>> getInstalledGraphicsPacks,
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
                    DesktopIndex = entry.Workspace.Desktop,
                    DesktopCount = getDesktopCount(),
                    DesktopAvailable = getDesktopAvailable(),
                    IconStyle = entry.IconStyle,
                    InstalledGraphicsPacks = getInstalledGraphicsPacks(),
                };
                var items = WorkspaceMenuModel.Build(state);
                MenuRenderer.Apply(menu, items, tag => OnClick(tag, onUnpin, onAssignDesktop, onSetDesktop, onSetIconStyle), logger);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Error rebuilding workspace menu for {Name}", entry.Workspace.Name);
            }
        };
        return menu;
    }

    private static void OnClick(string tag, Action<string> onUnpin, Action onAssignDesktop, Action<int> onSetDesktop, Action<string?> onSetIconStyle)
    {
        if (tag == "assign-desktop")
            onAssignDesktop();
        else if (tag.StartsWith("unpin:", StringComparison.Ordinal))
            onUnpin(tag["unpin:".Length..]);
        else if (tag.StartsWith("set-desktop:", StringComparison.Ordinal))
        {
            if (int.TryParse(tag["set-desktop:".Length..], out var index))
                onSetDesktop(index);
        }
        else if (tag.StartsWith("set-icon-style:", StringComparison.Ordinal))
        {
            var style = tag["set-icon-style:".Length..];
            onSetIconStyle(style == "(default)" ? null : style);
        }
    }
}
