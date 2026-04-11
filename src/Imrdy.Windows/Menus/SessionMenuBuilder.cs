using Imrdy.Core.Menus;
using Imrdy.Windows.Models;
using Microsoft.Extensions.Logging;

namespace Imrdy.Windows.Menus;

internal sealed record SessionMenuCallbacks(
    Action OnSwitchDesktop,
    Action OnAssignDesktop,
    Action<int?> OnSetDesktop,
    Action<string?> OnSetPack,
    Action<string?> OnSetIconStyle,
    Action OnPinWorkspace,
    Action OnUnpinWorkspace,
    Action OnClear,
    Action OnClearAll,
    Action OnDumpState,
    Action OnExit);

internal static class SessionMenuBuilder
{
    public static ContextMenuStrip Create(
        SessionEntry entry,
        SessionMenuCallbacks callbacks,
        Func<IReadOnlyList<string>> getInstalledPacks,
        Func<IReadOnlyList<string>> getInstalledGraphicsPacks,
        Func<int?> getDesktopCount,
        Func<bool> getDesktopAvailable,
        Func<bool> getIsPinned,
        ILogger? logger = null)
    {
        var menu = new ContextMenuStrip();
        menu.Opening += (_, _) =>
        {
            try
            {
                var state = new SessionMenuState
                {
                    SessionId = entry.SessionId,
                    Status = entry.State.Status,
                    Project = entry.State.Project,
                    DesktopIndex = entry.DesktopIndex,
                    SoundPack = entry.SoundPack,
                    IconStyle = entry.IconStyle,
                    InstalledPacks = getInstalledPacks(),
                    InstalledGraphicsPacks = getInstalledGraphicsPacks(),
                    DesktopCount = getDesktopCount(),
                    DesktopAvailable = getDesktopAvailable(),
                    IsPinned = getIsPinned(),
                };
                var items = SessionMenuModel.Build(state);
                MenuRenderer.Apply(menu, items, tag => OnClick(tag, callbacks), logger);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Error rebuilding session menu for {SessionId}", entry.SessionId);
            }
        };
        return menu;
    }

    private static void OnClick(string tag, SessionMenuCallbacks callbacks)
    {
        if (tag == "switch-desktop")
            callbacks.OnSwitchDesktop();
        else if (tag == "assign-desktop")
            callbacks.OnAssignDesktop();
        else if (tag == "set-desktop:auto")
            callbacks.OnSetDesktop(null);
        else if (tag.StartsWith("set-desktop:", StringComparison.Ordinal))
        {
            if (int.TryParse(tag["set-desktop:".Length..], out var index))
                callbacks.OnSetDesktop(index);
        }
        else if (tag.StartsWith("set-pack:", StringComparison.Ordinal))
        {
            var packName = tag["set-pack:".Length..];
            callbacks.OnSetPack(packName == "(none)" ? null : packName);
        }
        else if (tag.StartsWith("set-icon-style:", StringComparison.Ordinal))
        {
            var styleName = tag["set-icon-style:".Length..];
            callbacks.OnSetIconStyle(styleName == "(default)" ? null : styleName);
        }
        else if (tag == "pin-workspace")
            callbacks.OnPinWorkspace();
        else if (tag == "unpin-workspace")
            callbacks.OnUnpinWorkspace();
        else if (tag == "clear")
            callbacks.OnClear();
        else if (tag == "clear-all")
            callbacks.OnClearAll();
        else if (tag == "dump-state")
            callbacks.OnDumpState();
        else if (tag == "exit")
            callbacks.OnExit();
    }
}
