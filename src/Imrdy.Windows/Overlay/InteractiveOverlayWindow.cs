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

    public InteractiveOverlayWindow(OverlayConfig config, ISessionInteractionRouter router, ILoggerFactory loggerFactory, GraphicsPackLoader graphicsPackLoader)
        : base(config, loggerFactory, graphicsPackLoader)
    {
        _router = router;
        // Hand cursor signals clickability. WM_NCHITTEST returns HTTRANSPARENT over gaps,
        // so the OS routes the cursor to the window below there — the hand only appears on icons.
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && HitIconIndex(e.X, out var idx) && idx < _items.Count)
        {
            var item = _items[idx];
            try
            {
                if (item.ItemType == DisplayItemType.Session)
                    _router.ActivateSession(item.Id);
                else
                    _router.ActivateWorkspace(item.Id);
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
            m.Result = HitIconIndex(sx, out _) ? (IntPtr)HTCLIENT : (IntPtr)HTTRANSPARENT;
            return;
        }

        base.WndProc(ref m);
    }
}
