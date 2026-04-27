using System.Drawing;
using System.Runtime.InteropServices;

namespace Imrdy.Windows.Desktop;

/// <summary>
/// P/Invoke declarations for layered overlay window management.
/// Used by OverlayWindow for per-pixel alpha rendering via UpdateLayeredWindow.
/// Separated from PInvokeWindow per D16 — different concern (overlay rendering vs. window focusing).
/// </summary>
internal static class PInvokeOverlay
{
    // GetWindowRect for reading the actual HWND screen rect.
    // WinForms' Form.Bounds caches its value in internal fields that only refresh on
    // WM_WINDOWPOSCHANGED; UpdateLayeredWindow positions the HWND via Win32 without
    // reliably firing that message in a way WinForms catches for layered+toolwindow forms.
    // SetBounds() also fails to refresh the cache in this configuration.
    // Go straight to Win32 for the ground-truth rect.
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    // Separate struct for WindowFromPoint to avoid name collision with the existing POINT
    // used by UpdateLayeredWindow/ScreenToClient.
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT_WFP { public int X; public int Y; }

    [DllImport("user32.dll", EntryPoint = "WindowFromPoint")]
    private static extern IntPtr WindowFromPoint(POINT_WFP pt);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

    /// <summary>
    /// Returns the actual screen rectangle of the HWND via Win32 GetWindowRect.
    /// Bypasses WinForms' cached <see cref="System.Windows.Forms.Form.Bounds"/>, which
    /// does not refresh after <see cref="SetBitmap"/> positioning on layered+toolwindow
    /// forms. Returns <see cref="Rectangle.Empty"/> if the call fails or hwnd is zero.
    /// </summary>
    public static Rectangle GetActualWindowRect(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return Rectangle.Empty;
        if (!GetWindowRect(hwnd, out var r)) return Rectangle.Empty;
        return new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    /// <summary>
    /// Returns the HWND of the window directly under the given screen point via
    /// <c>WindowFromPoint</c>. Used for z-order hit-testing: <c>Rectangle.Contains</c>
    /// alone is insufficient because other topmost windows (taskbar popups, system shells,
    /// dragged windows) can geometrically overlap the overlay's screen rect even when our
    /// overlay isn't the visually-topmost window at that point.
    /// Returns <see cref="IntPtr.Zero"/> if no window is found.
    /// </summary>
    public static IntPtr WindowAtPoint(Point screenPoint)
    {
        var p = new POINT_WFP { X = screenPoint.X, Y = screenPoint.Y };
        return WindowFromPoint(p);
    }

    // GDI device context
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    // Layered window
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateLayeredWindow(
        IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pptSrc, uint crKey,
        ref BLENDFUNCTION pblend, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int Width, Height; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;              // AC_SRC_OVER = 0
        public byte BlendFlags;           // 0
        public byte SourceConstantAlpha;  // 255 = per-pixel alpha
        public byte AlphaFormat;          // AC_SRC_ALPHA = 1
    }

    // Window extended style constants
    public const int WS_EX_LAYERED    = 0x80000;
    public const int WS_EX_TRANSPARENT = 0x20;
    public const int WS_EX_NOACTIVATE  = 0x8000000;
    public const int WS_EX_TOOLWINDOW  = 0x80;

    // UpdateLayeredWindow constants
    public const byte AC_SRC_OVER  = 0;
    public const byte AC_SRC_ALPHA = 1;
    public const uint ULW_ALPHA    = 2;

    /// <summary>
    /// Converts screen coordinates to client coordinates.
    /// Used by WM_NCHITTEST whose lParam carries screen coords.
    /// </summary>
    public static bool ScreenToClientPoint(IntPtr hwnd, ref int x, ref int y)
    {
        var p = new POINT { X = x, Y = y };
        var ok = ScreenToClient(hwnd, ref p);
        if (ok) { x = p.X; y = p.Y; }
        return ok;
    }

    /// <summary>
    /// Decodes a Win32 message lParam carrying packed (x, y) coordinates.
    /// Uses (int)(nint) cast for 64-bit safety (NOT IntPtr.ToInt32() which
    /// throws OverflowException when upper 32 bits are non-zero).
    /// Sign-extends LOWORD/HIWORD because Win32 treats them as signed shorts —
    /// negative values legitimately occur on multi-monitor setups where the
    /// window sits on a monitor positioned left/above the primary.
    /// </summary>
    /// <remarks>
    /// WM_NCHITTEST lParam = SCREEN coords (call <see cref="ScreenToClientPoint"/> after).
    /// WM_LBUTTONDOWN / WM_RBUTTONUP lParam = CLIENT coords (use directly).
    /// </remarks>
    public static (int X, int Y) DecodeLParamPoint(IntPtr lParam)
    {
        var lp = (int)(nint)lParam;
        int x = lp & 0xFFFF;
        int y = (lp >> 16) & 0xFFFF;
        if (x >= 0x8000) x -= 0x10000;
        if (y >= 0x8000) y -= 0x10000;
        return (x, y);
    }

    /// <summary>
    /// Updates the layered window surface with the given bitmap at the specified screen location.
    /// Uses premultiplied alpha via GetHbitmap(Color.FromArgb(0)) for correct per-pixel transparency.
    /// </summary>
    /// <remarks>
    /// DC ownership rules:
    ///   - Screen DC (from Graphics.FromHwnd): released via ReleaseHdc — NOT DeleteDC.
    ///   - Memory DC (from CreateCompatibleDC): released via DeleteDC.
    ///   - Bitmap handle (from GetHbitmap): freed via DeleteObject after restoring original selection.
    /// </remarks>
    public static void SetBitmap(IntPtr hwnd, Bitmap bitmap, Point location)
    {
        using var screenGraphics = Graphics.FromHwnd(IntPtr.Zero);
        var screenDc = screenGraphics.GetHdc();
        var memDc = IntPtr.Zero;
        var hBitmap = IntPtr.Zero;
        var oldBitmap = IntPtr.Zero;
        try
        {
            memDc = CreateCompatibleDC(screenDc);
            hBitmap = bitmap.GetHbitmap(Color.FromArgb(0)); // premultiplied alpha
            oldBitmap = SelectObject(memDc, hBitmap);

            var ptDst = new POINT { X = location.X, Y = location.Y };
            var ptSrc = new POINT { X = 0, Y = 0 };
            var size = new SIZE { Width = bitmap.Width, Height = bitmap.Height };
            var blend = new BLENDFUNCTION
            {
                BlendOp = AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = AC_SRC_ALPHA
            };

            UpdateLayeredWindow(hwnd, screenDc, ref ptDst, ref size,
                memDc, ref ptSrc, 0, ref blend, ULW_ALPHA);
        }
        finally
        {
            if (oldBitmap != IntPtr.Zero) SelectObject(memDc, oldBitmap); // restore before DeleteDC
            if (hBitmap != IntPtr.Zero)   DeleteObject(hBitmap);
            if (memDc != IntPtr.Zero)     DeleteDC(memDc);          // DeleteDC for CreateCompatibleDC
            screenGraphics.ReleaseHdc(screenDc);                     // ReleaseHdc for FromHwnd DC
        }
    }

}
