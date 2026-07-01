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
                        onLaunchPreview, onCloseAllPreviews, logger),
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
            else if (await TryHandleOverlayTag(tag, state, onConfigChanged))
            {
                // overlay tag handled
            }
            else if (tag == "toggle-tray")
            {
                await Task.Run(() => ConfigReader.Update(c => c with { Tray = c.Tray with { Enabled = !state.Config.Tray.Enabled } }));
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

    /// <summary>
    /// Handles overlay-related menu tags. Returns <c>true</c> when the tag was recognized
    /// (regardless of whether a mutation occurred), <c>false</c> when it is not an overlay tag.
    /// On int-parse failure the tag is still recognized — returns <c>true</c> without mutating.
    /// </summary>
    internal static async Task<bool> TryHandleOverlayTag(
        string tag,
        ControllerMenuState state,
        Action<ImrdyConfig> onConfigChanged)
    {
        if (tag == "toggle-overlay")
        {
            await Task.Run(() => ConfigReader.Update(c => c with { Overlay = c.Overlay with { Enabled = !c.Overlay.Enabled } }));
            onConfigChanged(ConfigReader.Read());
            return true;
        }
        if (tag == "toggle-overlay-lock")
        {
            await Task.Run(() => ConfigReader.Update(c => c with { Overlay = c.Overlay with { Locked = !c.Overlay.Locked } }));
            onConfigChanged(ConfigReader.Read());
            return true;
        }
        if (tag.StartsWith("set-overlay-position:", StringComparison.Ordinal))
        {
            var position = tag["set-overlay-position:".Length..];
            await Task.Run(() => ConfigReader.Update(c => c with { Overlay = c.Overlay with { Position = position } }));
            onConfigChanged(ConfigReader.Read());
            return true;
        }
        if (tag.StartsWith("set-overlay-size:", StringComparison.Ordinal))
        {
            if (!int.TryParse(tag["set-overlay-size:".Length..], out var size)) return true;
            await Task.Run(() => ConfigReader.Update(c => c with { Overlay = c.Overlay with { Size = size } }));
            onConfigChanged(ConfigReader.Read());
            return true;
        }
        if (tag.StartsWith("set-overlay-spacing:", StringComparison.Ordinal))
        {
            if (!int.TryParse(tag["set-overlay-spacing:".Length..], out var spacing)) return true;
            await Task.Run(() => ConfigReader.Update(c => c with { Overlay = c.Overlay with { Spacing = spacing } }));
            onConfigChanged(ConfigReader.Read());
            return true;
        }
        if (tag.StartsWith("set-overlay-monitor:", StringComparison.Ordinal))
        {
            if (!int.TryParse(tag["set-overlay-monitor:".Length..], out var monitor)) return true;
            await Task.Run(() => ConfigReader.Update(c => c with { Overlay = c.Overlay with { Monitor = monitor } }));
            onConfigChanged(ConfigReader.Read());
            return true;
        }
        return false;
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
