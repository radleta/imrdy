using Imrdy.Core;
using Imrdy.Core.Menus;
using Microsoft.Extensions.Logging;

namespace Imrdy.Windows.Menus;

/// <summary>
/// Builds the overlay gutter right-click context menu.
/// Renders <see cref="ControllerMenuModel.BuildOverlaySubmenu"/> children flat
/// (no nested "Overlay" parent) and dispatches via
/// <see cref="ControllerMenuBuilder.TryHandleOverlayTag"/>.
/// </summary>
public static class OverlayMenuBuilder
{
    public static ContextMenuStrip Create(
        Func<ControllerMenuState> stateProvider,
        Action<ImrdyConfig> onConfigChanged,
        ILogger? logger)
    {
        var menu = new ContextMenuStrip();
        // Diagnostic-only (Step 07): see SessionMenuBuilder's Opening comment for the full
        // rationale — same start/end + item-count + e.Cancel logging, correlated by
        // menu.GetHashCode() with TrayApp.ShowContextMenuAt's before/after Show() logs. This is
        // the overlay gutter's own menu, the one under live investigation for the ~50%
        // dismiss-without-opening defect.
        menu.Opening += (_, e) =>
        {
            logger?.LogDebug("OverlayMenuBuilder.Opening: start, menu={MenuId}", menu.GetHashCode());
            try
            {
                var state = stateProvider();
                var children = ControllerMenuModel.BuildOverlaySubmenu(state).Children;
                MenuRenderer.Apply(menu, children,
                    tag => OnClick(tag, state, onConfigChanged, logger),
                    logger);

                // The fix (Step 08 — this is the menu that started the investigation):
                // ContextMenuStrip.OnOpening pre-sets e.Cancel = true whenever Items.Count == 0,
                // which is always true on the FIRST show of this freshly-constructed menu since
                // items are built here, not at Create() time. Left uncleared, WinForms refuses
                // to display the menu regardless of how many items were just added. Only
                // reached when the rebuild above completed without throwing — see
                // MenuOpeningPolicy's remarks for why an exception must skip this line.
                if (MenuOpeningPolicy.ShouldClearCancel(menu.Items.Count))
                {
                    e.Cancel = false;
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(
                    ex,
                    "OverlayMenuBuilder.Opening: exception rebuilding overlay menu — menu.Items left stale/empty, Show() will likely refuse to display");
            }
            logger?.LogDebug(
                "OverlayMenuBuilder.Opening: end, menu={MenuId}, items={ItemCount}, cancel={Cancel}",
                menu.GetHashCode(), menu.Items.Count, e.Cancel);
        };
        return menu;
    }

    private static async void OnClick(
        string tag,
        ControllerMenuState state,
        Action<ImrdyConfig> onConfigChanged,
        ILogger? logger)
    {
        try
        {
            await ControllerMenuBuilder.TryHandleOverlayTag(tag, state, onConfigChanged, logger);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "overlay menu action failed");
        }
    }
}
