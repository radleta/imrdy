using System.ComponentModel;
using System.Windows.Forms;
using Imrdy.Core;
using Imrdy.Core.Display;
using Imrdy.Core.Graphics;
using Imrdy.Core.Icons;
using Imrdy.Windows.Desktop;
using Imrdy.Windows.Interaction;
using Microsoft.Extensions.Logging;

namespace Imrdy.Windows.Overlay;

/// <summary>
/// Input-handling overlay. Activatable (no <c>WS_EX_NOACTIVATE</c>) so right-clicks
/// transfer foreground to it naturally — that's how WinForms' standard context-menu
/// pipeline gets the foreground anchor it needs for hover hot-tracking.
///
/// Mouse handling uses the vanilla <see cref="OnMouseDown"/> / <see cref="OnMouseUp"/>
/// overrides, NOT raw <c>WM_LBUTTONDOWN</c> / <c>WM_RBUTTONUP</c> interception. This keeps
/// the WinForms event pipeline intact (Click, MouseClick, focus bookkeeping, the
/// <c>WM_RBUTTONUP</c>→<c>WM_CONTEXTMENU</c> generation in <c>DefWindowProc</c>).
///
/// The only thing we MUST intercept at the message-pump level is <c>WM_NCHITTEST</c>:
/// that's the click-through policy (<c>HTCLIENT</c> on icons, <c>HTTRANSPARENT</c>
/// on gaps) and there's no managed equivalent.
/// </summary>
internal sealed class InteractiveOverlayWindow : OverlayWindowBase
{
    private readonly ISessionInteractionRouter _router;

    /// <summary>
    /// Set to <c>true</c> by <see cref="Dashboard.HoverDashboardController"/> while the
    /// hover dashboard is visible. When true, WM_NCHITTEST returns HTCLIENT across the
    /// full icon row (not just over icons) so the cursor remains captive during icon-gap
    /// crossings — preventing flicker as the user moves from an icon toward the dashboard.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsDashboardHoverActive { get; set; } = false;

    /// <summary>
    /// Raised after a left-click successfully dispatches a session or workspace activation.
    /// Subscribers (notably <see cref="Dashboard.HoverDashboardController"/>) can use this
    /// to dismiss any preview UI that should not outlive the activating click.
    /// Right-click does not raise this event — menu dismissal is handled by WinForms naturally.
    /// </summary>
    public event Action? SurfaceInteracted;

    public InteractiveOverlayWindow(OverlayConfig config, ISessionInteractionRouter router, ILoggerFactory loggerFactory, GraphicsPackLoader graphicsPackLoader)
        : base(config, loggerFactory, graphicsPackLoader)
    {
        _router = router;
        // Hand cursor signals clickability. WM_NCHITTEST returns HTTRANSPARENT over gaps,
        // so the OS routes the cursor to the window below there — the hand only appears on icons.
        Cursor = Cursors.Hand;
    }

    /// <summary>
    /// Maps a screen-coordinate point to the session id of the icon under it.
    /// Returns <c>false</c> when the point is not over a session icon (gap, workspace item,
    /// or outside the overlay entirely).
    /// </summary>
    public bool TryGetSessionIdAtScreenPoint(Point screenPt, out string sessionId)
    {
        sessionId = string.Empty;

        int cx = screenPt.X, cy = screenPt.Y;
        if (!PInvokeOverlay.ScreenToClientPoint(Handle, ref cx, ref cy))
            return false;

        if (!HitIconIndex(cx, out var index))
            return false;

        if (index < 0 || index >= _items.Count)
            return false;

        var item = _items[index];
        if (item.ItemType != DisplayItemType.Session)
            return false;

        sessionId = item.Id;
        return true;
    }

    /// <summary>
    /// Maps an already-converted client-X coordinate to the <see cref="DisplayItem"/> at that
    /// position. Used by <see cref="Dashboard.HoverDashboardControllerBase"/>-derived controllers
    /// whose <c>TryHitTestForOurDomain</c> override has already performed the
    /// screen→client conversion via <see cref="Desktop.PInvokeOverlay.ScreenToClientPoint"/>.
    /// </summary>
    /// <param name="clientX">Client X coordinate (already converted from screen).</param>
    /// <param name="item">
    /// The resolved <see cref="DisplayItem"/> when a hit is found; <c>null</c> otherwise.
    /// </param>
    /// <param name="hitIndex">Slot index when a hit is found; <c>-1</c> otherwise.</param>
    /// <returns><c>true</c> when a <see cref="DisplayItem"/> occupies <paramref name="clientX"/>.</returns>
    public bool TryHitTestAtClient(int clientX, out DisplayItem? item, out int hitIndex)
    {
        item = null;
        hitIndex = -1;

        if (!HitIconIndex(clientX, out var index))
            return false;

        if (index < 0 || index >= _items.Count)
            return false;

        item = _items[index];
        hitIndex = index;
        return true;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _logger.LogDebug("Overlay: OnMouseDown button={Button} x={X} y={Y}", e.Button, e.X, e.Y);
        if (e.Button == MouseButtons.Left && HitIconIndex(e.X, out var idx) && idx < _items.Count)
        {
            var item = _items[idx];
            try
            {
                if (item.ItemType == DisplayItemType.Session)
                    _router.ActivateSession(item.Id);
                else
                    _router.ActivateWorkspace(item.Id);
                _logger.LogDebug("Overlay: router dispatch succeeded for {ItemType} {Id}, firing SurfaceInteracted", item.ItemType, item.Id);
                var subscriberCount = SurfaceInteracted?.GetInvocationList().Length ?? 0;
                _logger.LogDebug("Overlay: SurfaceInteracted firing to {Count} subscriber(s)", subscriberCount);
                SurfaceInteracted?.Invoke();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Overlay left-click dispatch failed for {ItemType} {Id}",
                    item.ItemType, item.Id);
            }
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right && HitIconIndex(e.X, out var idx) && idx < _items.Count)
        {
            var item = _items[idx];
            try
            {
                var anchor = MenuAnchor.AtControl(this, e.Location);
                if (item.ItemType == DisplayItemType.Session)
                    _router.OpenSessionMenu(item.Id, anchor);
                else
                    _router.OpenWorkspaceMenu(item.Id, anchor);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Overlay right-click menu failed for {ItemType} {Id}",
                    item.ItemType, item.Id);
            }
        }
        base.OnMouseUp(e);
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_NCHITTEST = 0x0084;
        const int HTCLIENT = 1;
        const int HTTRANSPARENT = -1;

        if (m.Msg == WM_NCHITTEST)
        {
            var (sx, sy) = PInvokeOverlay.DecodeLParamPoint(m.LParam); // SCREEN coords
            PInvokeOverlay.ScreenToClientPoint(Handle, ref sx, ref sy);

            if (IsDashboardHoverActive)
            {
                // While the hover dashboard is visible, return HTCLIENT across the full
                // icon row so the cursor doesn't fall through gaps during traversal to
                // the dashboard. Existing click handlers (OnMouseDown/OnMouseUp) still
                // guard on HitIconIndex — non-icon clicks are ignored there, not here.
                m.Result = (IntPtr)HTCLIENT;
            }
            else
            {
                m.Result = HitIconIndex(sx, out _) ? (IntPtr)HTCLIENT : (IntPtr)HTTRANSPARENT;
            }
            return;
        }

        base.WndProc(ref m);
    }
}
