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
        menu.Opening += (_, _) =>
        {
            try
            {
                var state = stateProvider();
                var children = ControllerMenuModel.BuildOverlaySubmenu(state).Children;
                MenuRenderer.Apply(menu, children,
                    tag => OnClick(tag, state, onConfigChanged, logger),
                    logger);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Error rebuilding overlay menu");
            }
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
            await ControllerMenuBuilder.TryHandleOverlayTag(tag, state, onConfigChanged);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "overlay menu action failed");
        }
    }
}
