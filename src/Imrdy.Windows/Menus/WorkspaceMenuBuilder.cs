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
        // Diagnostic-only (Step 07): see SessionMenuBuilder's Opening comment for the full
        // rationale — same start/end + item-count + e.Cancel logging, correlated by
        // menu.GetHashCode() with TrayApp.ShowContextMenuAt's before/after Show() logs.
        menu.Opening += (_, e) =>
        {
            logger?.LogDebug(
                "WorkspaceMenuBuilder.Opening: start, menu={MenuId}, name={Name}",
                menu.GetHashCode(), entry.Workspace.Name);
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

                // The fix (Step 08): ContextMenuStrip.OnOpening pre-sets e.Cancel = true
                // whenever Items.Count == 0 — always true on the FIRST show of this
                // freshly-constructed menu, since items are built here, not at Create() time.
                // Left uncleared, WinForms refuses to display the menu no matter how many
                // items were just added. Only reached when the rebuild above completed
                // without throwing — see MenuOpeningPolicy's remarks for why an exception
                // must skip this line rather than clear Cancel on a partial/stale collection.
                if (MenuOpeningPolicy.ShouldClearCancel(menu.Items.Count))
                {
                    e.Cancel = false;
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(
                    ex,
                    "WorkspaceMenuBuilder.Opening: exception rebuilding menu for {Name} — menu.Items left stale/empty, Show() will likely refuse to display",
                    entry.Workspace.Name);
            }
            logger?.LogDebug(
                "WorkspaceMenuBuilder.Opening: end, menu={MenuId}, items={ItemCount}, cancel={Cancel}",
                menu.GetHashCode(), menu.Items.Count, e.Cancel);
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
