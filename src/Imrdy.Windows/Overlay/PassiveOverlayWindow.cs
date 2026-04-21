using Imrdy.Core;
using Imrdy.Core.Graphics;
using Imrdy.Core.Icons;
using Imrdy.Windows.Desktop;
using Microsoft.Extensions.Logging;

namespace Imrdy.Windows.Overlay;

/// <summary>
/// Purely visual overlay. WS_EX_TRANSPARENT set at creation — the OS treats the
/// entire window as click-through, mouse events never reach our WndProc. No input
/// handling, no context menus, no runtime style toggling. The simplest possible
/// variant: it exists only to display icons.
/// </summary>
internal sealed class PassiveOverlayWindow : OverlayWindowBase
{
    public PassiveOverlayWindow(OverlayConfig config, ILoggerFactory loggerFactory, GraphicsPackLoader graphicsPackLoader)
        : base(config, loggerFactory, graphicsPackLoader)
    {
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= PInvokeOverlay.WS_EX_TRANSPARENT;
            cp.ExStyle |= PInvokeOverlay.WS_EX_NOACTIVATE;
            return cp;
        }
    }
}
