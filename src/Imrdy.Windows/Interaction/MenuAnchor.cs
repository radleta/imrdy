using System.Drawing;
using System.Windows.Forms;

namespace Imrdy.Windows.Interaction;

/// <summary>
/// Value type describing where to anchor a <see cref="ContextMenuStrip"/> when the
/// interaction router opens a menu. The two anchoring modes map directly to the two
/// surfaces that can source a right-click:
/// <list type="bullet">
///   <item><description><see cref="AtTrayIcon"/> — the shell-delivered <see cref="NotifyIcon"/> click;
///     dispatched via <c>NotifyIconMenuHost</c> (reflection-based private API).</description></item>
///   <item><description><see cref="AtControl"/> — a WinForms <see cref="Control"/> (the overlay form)
///     with a real client-coordinate point; dispatched via <c>menu.Show(owner, location)</c>.</description></item>
/// </list>
/// Keep this as the single abstraction — callers should never reach for <c>NotifyIconMenuHost</c>
/// or <c>menu.Show</c> directly, only through the router.
/// </summary>
internal readonly record struct MenuAnchor
{
    public NotifyIcon? TrayIcon { get; }
    public Control? Owner { get; }
    public Point Location { get; }

    private MenuAnchor(NotifyIcon? trayIcon, Control? owner, Point location)
    {
        TrayIcon = trayIcon;
        Owner = owner;
        Location = location;
    }

    public static MenuAnchor AtTrayIcon(NotifyIcon icon) => new(icon, null, default);

    /// <summary>
    /// Anchors a menu to a WinForms <see cref="Control"/> — the overlay form — at a real
    /// client-coordinate point; dispatched via <c>menu.Show(owner, location)</c>
    /// (<c>TrayApp.ShowContextMenuAt</c>'s <c>AtControl</c> branch). This relies on
    /// <paramref name="owner"/> actually holding foreground once the menu opens —
    /// <c>ToolStripManager</c>'s modal menu filter force-closes a <see cref="ContextMenuStrip"/>
    /// whose owner isn't foreground the instant any unrelated activation change happens
    /// elsewhere on the desktop (<c>ToolStripDropDownCloseReason.AppFocusChange</c>).
    ///
    /// The overlay is NEVER activated by this path, or by any other: <c>OverlayPanel.WndProc</c>
    /// returns <c>MA_NOACTIVATE</c> unconditionally for every interaction, right-click included
    /// (see its <c>CreateParams</c>/<c>WndProc</c> docs) — an earlier attempt tried an
    /// MA_ACTIVATE-on-right-click exception there and it did not work, because
    /// <c>WM_MOUSEACTIVATE</c> activation is not the same thing as owning foreground *input*,
    /// which is what <c>SetForegroundWindow</c> and <c>ContextMenuStrip.Show</c> actually
    /// require. Because the overlay is never activatable, <c>AtControl</c> depends entirely on
    /// <c>ShowContextMenuAt</c> performing the full documented Win32 sequence itself — there is
    /// no WinForms-native fallback that makes this "just work":
    /// <list type="number">
    ///   <item><description><c>PInvokeWindow.SetForegroundWindow(owner.Handle)</c>, wrapped in
    ///     <c>PInvokeWindow.InvokeWithForegroundAttached</c> — the same AttachThreadInput dance
    ///     <c>NotifyIconMenuHost</c> uses for the tray-icon path — so the call succeeds even
    ///     though the calling thread doesn't natively own foreground input.</description></item>
    ///   <item><description><c>menu.Show(owner, location)</c>.</description></item>
    ///   <item><description><c>PInvokeWindow.PostMessage(owner.Handle, WM_NULL, ...)</c>,
    ///     immediately after — Microsoft's documented KB135788 fix for exactly this
    ///     "notify-icon/ContextMenuStrip menu appears and immediately disappears on the second
    ///     display" failure, which is the steady state here once the overlay already owns
    ///     foreground from a prior interaction. Skipping this step is what caused the overlay's
    ///     right-click menu to work on only ~50% of clicks — it was implemented once, then
    ///     removed by a reader who read only steps 1 and 2 above and assumed they were
    ///     sufficient. They are not; do not remove step 3 again.</description></item>
    /// </list>
    /// <c>ShowContextMenuAt</c> also captures and validates whatever window held foreground
    /// just before step 1 (rejecting invalid/own-process/non-caption candidates and falling
    /// back to the last known-good one — see <c>TrayApp.SampleForegroundForRestoreTracking</c>
    /// and <c>CaptureForegroundForRestore</c>), then restores it once the menu closes
    /// (<c>RestorePendingForeground</c>), so the focus theft lasts only as long as the menu is
    /// open.
    /// </summary>
    public static MenuAnchor AtControl(Control owner, Point clientLocation) => new(null, owner, clientLocation);
}
