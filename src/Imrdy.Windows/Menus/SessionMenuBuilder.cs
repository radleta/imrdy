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
        // Diagnostic-only (Step 07): log start/end of every Opening rebuild alongside the item
        // count produced and e.Cancel on exit, correlated by menu.GetHashCode() with the
        // before/after Show() logs in TrayApp.ShowContextMenuAt. If Show() reports
        // Visible=false with no "Opening: start" line between the two Show logs, Opening never
        // fired — a Win32-level refusal. If it fired but "end" reports items=0 or cancel=true,
        // that is the empty/cancelled-menu hypothesis this step exists to test.
        menu.Opening += (_, e) =>
        {
            logger?.LogDebug(
                "SessionMenuBuilder.Opening: start, menu={MenuId}, sessionId={SessionId}",
                menu.GetHashCode(), entry.SessionId);
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

                // The fix (Step 08): ContextMenuStrip.OnOpening pre-sets e.Cancel = true
                // whenever Items.Count == 0 — which is always true on the FIRST show of this
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
                    "SessionMenuBuilder.Opening: exception rebuilding menu for {SessionId} — menu.Items left stale/empty, Show() will likely refuse to display",
                    entry.SessionId);
            }
            logger?.LogDebug(
                "SessionMenuBuilder.Opening: end, menu={MenuId}, items={ItemCount}, cancel={Cancel}",
                menu.GetHashCode(), menu.Items.Count, e.Cancel);
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
