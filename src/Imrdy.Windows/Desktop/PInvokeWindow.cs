using System.Runtime.InteropServices;

namespace Imrdy.Windows.Desktop;

/// <summary>
/// P/Invoke declarations for window management.
/// Used by ComVirtualDesktop for window focusing and desktop switching.
/// </summary>
internal static class PInvokeWindow
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo,
        [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AllowSetForegroundWindow(int dwProcessId);

    public const int ASFW_ANY = -1;

    private const uint MONITOR_DEFAULTTOPRIMARY = 1;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    /// <summary>
    /// Returns the HMONITOR for the primary display. Used by undocumented
    /// IVirtualDesktopManagerInternal methods that require a monitor handle
    /// (GetDesktops, GetCurrentDesktop) on some Windows builds.
    /// </summary>
    public static IntPtr GetPrimaryMonitor() => MonitorFromWindow(IntPtr.Zero, MONITOR_DEFAULTTOPRIMARY);

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const byte VK_MENU = 0x12; // Alt key
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    /// <summary>
    /// Steals foreground activation rights by sending a synthetic Alt key press.
    /// Windows grants SetForegroundWindow rights to processes that receive keyboard input.
    /// Call this before ForceForeground when calling from a non-foreground context
    /// (e.g., balloon tip click, timer callback).
    /// </summary>
    public static void StealForegroundRights()
    {
        keybd_event(VK_MENU, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
        keybd_event(VK_MENU, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    public const int SW_RESTORE = 9;

    /// <summary>
    /// Robustly brings a window to the foreground using AttachThreadInput trick.
    /// Windows restricts SetForegroundWindow to the foreground process — this works around it.
    /// </summary>
    public static bool ForceForeground(IntPtr hWnd)
    {
        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == hWnd)
        {
            return true;
        }

        var currentThreadId = GetCurrentThreadId();
        var foregroundThreadId = GetWindowThreadProcessId(foregroundWindow, out _);
        var attached = false;

        try
        {
            if (currentThreadId != foregroundThreadId)
            {
                attached = AttachThreadInput(currentThreadId, foregroundThreadId, true);
            }

            // Only restore if minimized — don't un-maximize already-visible windows
            if (IsIconic(hWnd))
            {
                ShowWindow(hWnd, SW_RESTORE);
            }
            return SetForegroundWindow(hWnd);
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(currentThreadId, foregroundThreadId, false);
            }
        }
    }

    /// <summary>
    /// Finds the main window handle for a given process ID.
    /// Enumerates all top-level windows looking for one owned by the process.
    /// </summary>
    public static IntPtr FindMainWindowForProcess(int processId)
    {
        IntPtr foundWindow = IntPtr.Zero;

        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out var windowPid);
            if (windowPid == (uint)processId && IsWindowVisible(hWnd))
            {
                foundWindow = hWnd;
                return false; // Stop enumeration
            }

            return true; // Continue enumeration
        }, IntPtr.Zero);

        return foundWindow;
    }
}
