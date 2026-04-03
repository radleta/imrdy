using Imrdy.Core.Menus;
using Imrdy.Core.Sound;
using Microsoft.Extensions.Logging;

namespace Imrdy.Windows.Menus;

/// <summary>
/// Builds the controller tray icon context menu.
/// Rebuilt dynamically on each Opening event from in-memory state.
/// </summary>
internal static class ControllerMenuBuilder
{
    public static ContextMenuStrip Create(
        Func<ControllerMenuState> stateProvider,
        Action<SoundConfig> onConfigChanged,
        Action onExit,
        ILogger? logger = null)
    {
        var menu = new ContextMenuStrip();
        menu.Opening += (_, _) =>
        {
            try
            {
                var state = stateProvider();
                var items = ControllerMenuModel.Build(state);
                MenuRenderer.Apply(menu, items, tag => OnClick(tag, state, onConfigChanged, onExit, logger), logger);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Error rebuilding controller menu");
            }
        };
        return menu;
    }

    private static async void OnClick(
        string tag,
        ControllerMenuState state,
        Action<SoundConfig> onConfigChanged,
        Action onExit,
        ILogger? logger)
    {
        try
        {
            if (tag == "toggle-sound")
            {
                var newConfig = state.Config with { SoundEnabled = !state.Config.SoundEnabled };
                await Task.Run(() => SoundConfigWriter.Save(newConfig, Path.Combine(state.SoundsDir, "config.json")));
                onConfigChanged(newConfig);
            }
            else if (tag.StartsWith("switch-pack:", StringComparison.Ordinal))
            {
                var packName = tag["switch-pack:".Length..];
                var newConfig = state.Config with { Default = packName };
                await Task.Run(() => SoundConfigWriter.Save(newConfig, Path.Combine(state.SoundsDir, "config.json")));
                onConfigChanged(newConfig);
            }
            else if (tag == "open-config")
            {
                OpenFolder("explorer.exe", state.ConfigDir, logger);
            }
            else if (tag == "open-sounds")
            {
                OpenFolder("explorer.exe", state.SoundsDir, logger);
            }
            else if (tag == "open-log")
            {
                OpenFolder("explorer.exe", "/select," + state.LogPath, logger);
            }
            else if (tag == "exit")
            {
                onExit();
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error handling menu click: {Tag}", tag);
        }
    }

    private static void OpenFolder(string exe, string args, ILogger? logger)
    {
        System.Diagnostics.Process? proc = null;
        try
        {
            proc = System.Diagnostics.Process.Start(exe, args);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to open: {Exe} {Args}", exe, args);
        }
        finally
        {
            proc?.Dispose();
        }
    }
}
