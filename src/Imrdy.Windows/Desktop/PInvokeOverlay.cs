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

    // Window positioning (TopMost watchdog per D17)
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

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

    // SetWindowPos constants (TopMost watchdog)
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint SWP_NOMOVE    = 0x0002;
    public const uint SWP_NOSIZE    = 0x0001;
    public const uint SWP_NOACTIVATE = 0x0010;

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

    /// <summary>
    /// Re-asserts HWND_TOPMOST via SetWindowPos without activating the window.
    /// Called by the TopMost watchdog timer (D17) to recover from z-order displacement.
    /// </summary>
    public static void ReapplyTopMost(IntPtr hwnd)
    {
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }
}
