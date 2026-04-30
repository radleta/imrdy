using System.Diagnostics;
using Imrdy.Core;
using Imrdy.Core.Menus;
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
        Action<ImrdyConfig> onConfigChanged,
        Action<string> onSwitchSession,
        Action<string> onSwitchWorkspace,
        Action onExit,
        Action<string>? onLaunchPreview = null,
        Action? onCloseAllPreviews = null,
        Action? onRescanDistros = null,
        Action<bool>? onToggleWslWatchAll = null,
        Action<string, bool>? onToggleWslDistro = null,
        ILogger? logger = null)
    {
        var menu = new ContextMenuStrip();
        menu.Opening += (_, _) =>
        {
            try
            {
                var state = stateProvider();
                var items = ControllerMenuModel.Build(state);
                MenuRenderer.Apply(menu, items,
                    tag => OnClick(tag, state, onConfigChanged, onSwitchSession, onSwitchWorkspace, onExit,
                        onLaunchPreview, onCloseAllPreviews, onRescanDistros, onToggleWslWatchAll, onToggleWslDistro, logger),
                    logger);
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
        Action<ImrdyConfig> onConfigChanged,
        Action<string> onSwitchSession,
        Action<string> onSwitchWorkspace,
        Action onExit,
        Action<string>? onLaunchPreview,
        Action? onCloseAllPreviews,
        Action? onRescanDistros,
        Action<bool>? onToggleWslWatchAll,
        Action<string, bool>? onToggleWslDistro,
        ILogger? logger)
    {
        try
        {
            if (tag == "toggle-sound")
            {
                await Task.Run(() => ConfigReader.Update(c => c with { Sound = c.Sound with { Enabled = !state.Config.Sound.Enabled } }));
                onConfigChanged(ConfigReader.Read());
            }
            else if (tag.StartsWith("switch-pack:", StringComparison.Ordinal))
            {
                var packName = tag["switch-pack:".Length..];
                await Task.Run(() => ConfigReader.Update(c => c with { Sound = c.Sound with { DefaultPack = packName } }));
                onConfigChanged(ConfigReader.Read());
            }
            else if (tag.StartsWith("toggle-pack-enabled:", StringComparison.Ordinal))
            {
                var packName = tag["toggle-pack-enabled:".Length..];
                await Task.Run(() => ConfigReader.Update(c =>
                {
                    var disabled = c.Sound.DisabledPacks;
                    var isCurrentlyDisabled = disabled.Any(d =>
                        string.Equals(d, packName, StringComparison.OrdinalIgnoreCase));
                    var newDisabled = isCurrentlyDisabled
                        ? disabled.Where(d => !string.Equals(d, packName, StringComparison.OrdinalIgnoreCase)).ToList()
                        : [.. disabled, packName];
                    return c with { Sound = c.Sound with { DisabledPacks = newDisabled } };
                }));
                onConfigChanged(ConfigReader.Read());
            }
            else if (tag.StartsWith("switch-icon-style:", StringComparison.Ordinal))
            {
                var newStyle = tag["switch-icon-style:".Length..];
                await Task.Run(() => ConfigReader.Update(c => c with { Tray = c.Tray with { IconStyle = newStyle } }));
                onConfigChanged(ConfigReader.Read());
            }
            else if (tag == "toggle-overlay")
            {
                await Task.Run(() => ConfigReader.Update(c => c with { Overlay = c.Overlay with { Enabled = !state.Config.Overlay.Enabled } }));
                onConfigChanged(ConfigReader.Read());
            }
            else if (tag == "toggle-tray")
            {
                await Task.Run(() => ConfigReader.Update(c => c with { Tray = c.Tray with { Enabled = !state.Config.Tray.Enabled } }));
                onConfigChanged(ConfigReader.Read());
            }
            else if (tag == "toggle-overlay-interactive")
            {
                await Task.Run(() => ConfigReader.Update(c => c with { Overlay = c.Overlay with { Interactive = !(state.Config.Overlay.Interactive ?? true) } }));
                onConfigChanged(ConfigReader.Read());
            }
            else if (tag.StartsWith("switch-session:", StringComparison.Ordinal))
            {
                var sessionId = tag["switch-session:".Length..];
                if (!string.IsNullOrEmpty(sessionId))
                    onSwitchSession(sessionId);
            }
            else if (tag.StartsWith("switch-workspace:", StringComparison.Ordinal))
            {
                var workspacePath = tag["switch-workspace:".Length..];
                if (!string.IsNullOrEmpty(workspacePath))
                    onSwitchWorkspace(workspacePath);
            }
            else if (tag.StartsWith("set-overlay-position:", StringComparison.Ordinal))
            {
                var position = tag["set-overlay-position:".Length..];
                await Task.Run(() => ConfigReader.Update(c => c with { Overlay = c.Overlay with { Position = position } }));
                onConfigChanged(ConfigReader.Read());
            }
            else if (tag.StartsWith("set-overlay-size:", StringComparison.Ordinal))
            {
                if (!int.TryParse(tag["set-overlay-size:".Length..], out var size)) return;
                await Task.Run(() => ConfigReader.Update(c => c with { Overlay = c.Overlay with { Size = size } }));
                onConfigChanged(ConfigReader.Read());
            }
            else if (tag == "open-config")
            {
                OpenFolder("explorer.exe", ImrdyPaths.Home, logger);
            }
            else if (tag == "open-log")
            {
                OpenFolder("explorer.exe", "/select," + state.LogPath, logger);
            }
            else if (tag.StartsWith("dev-preview:", StringComparison.Ordinal))
            {
                var fixturePath = tag["dev-preview:".Length..];
                if (!string.IsNullOrEmpty(fixturePath))
                    onLaunchPreview?.Invoke(fixturePath);
            }
            else if (tag == "dev-preview-close-all")
            {
                onCloseAllPreviews?.Invoke();
            }
            else if (tag == "toggle-wsl-watch-all")
            {
                onToggleWslWatchAll?.Invoke(!state.Wsl!.WatchAll);
            }
            else if (tag.StartsWith("toggle-wsl-distro:", StringComparison.Ordinal))
            {
                var name = tag["toggle-wsl-distro:".Length..];
                var entry = state.Wsl?.Distros.FirstOrDefault(d => d.Name == name);
                if (entry is not null) onToggleWslDistro?.Invoke(name, !entry.Enabled);
            }
            else if (tag == "rescan-distros")
            {
                onRescanDistros?.Invoke();
            }
            else if (tag == "open-wsl-config")
            {
                Process.Start(new ProcessStartInfo(ImrdyPaths.WslDistros) { UseShellExecute = true })?.Dispose();
            }
            else if (tag == "view-wsl-log")
            {
                Process.Start(new ProcessStartInfo(ImrdyPaths.MonitorLog) { UseShellExecute = true })?.Dispose();
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
