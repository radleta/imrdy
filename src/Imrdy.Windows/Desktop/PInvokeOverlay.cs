using System.Drawing;
using System.Runtime.InteropServices;

namespace Imrdy.Windows.Desktop;

/// <summary>
/// P/Invoke declarations for overlay window hit-testing and message routing.
/// Separated from PInvokeWindow per D16 — different concern (overlay interaction vs. window focusing).
/// </summary>
internal static class PInvokeOverlay
{
    // Separate struct for WindowFromPoint to avoid name collision with the POINT
    // used by ScreenToClient.
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT_WFP { public int X; public int Y; }

    [DllImport("user32.dll", EntryPoint = "WindowFromPoint")]
    private static extern IntPtr WindowFromPoint(POINT_WFP pt);

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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern uint RegisterWindowMessage(string lpString);

    // Window extended style constant
    public const int WS_EX_TOOLWINDOW = 0x80;

    // GetSystemMetricsForDpi indices (D6) — the drag-threshold metrics.
    public const int SM_CXDRAG = 68;
    public const int SM_CYDRAG = 69;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);

    /// <summary>
    /// Per-monitor-DPI-correct system metric lookup (D6). Unlike
    /// <see cref="System.Windows.Forms.SystemInformation.DragSize"/>, which wraps the
    /// non-Per-Monitor-V2-aware <c>GetSystemMetrics</c>, this resolves the metric at the
    /// caller-supplied DPI (typically <c>Control.DeviceDpi</c>) — correct on a monitor
    /// whose DPI differs from the system DPI. Present since Win10 1607; always available
    /// on the 10.0.17763 target, so no fallback branch is needed.
    /// </summary>
    public static int GetSystemMetricForDpi(int nIndex, int dpi) =>
        GetSystemMetricsForDpi(nIndex, (uint)dpi);

    /// <summary>
    /// Converts screen coordinates to client coordinates.
    /// Used by overlay hit-testing where lParam carries screen coords.
    /// </summary>
    public static bool ScreenToClientPoint(IntPtr hwnd, ref int x, ref int y)
    {
        var p = new POINT { X = x, Y = y };
        var ok = ScreenToClient(hwnd, ref p);
        if (ok) { x = p.X; y = p.Y; }
        return ok;
    }
}
