using System.Runtime.InteropServices;

namespace Imrdy.Windows.Desktop;

/// <summary>
/// P/Invoke declarations and window-management helpers. Originally used by ComVirtualDesktop
/// for window focusing and desktop switching; also hosts the shared AttachThreadInput
/// foreground-attach dance (<see cref="InvokeWithForegroundAttached"/>) used by both
/// NotifyIconMenuHost (tray-icon menu path) and TrayApp.ShowContextMenuAt (overlay menu
/// path), and the capture-time foreground-restore-candidate validation it depends on.
/// </summary>
internal static class PInvokeWindow
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    // KB135788's documented fix for notify-icon/ContextMenuStrip-style menus that
    // appear-then-immediately-disappear on every other display when the owner window is
    // already foreground: post this benign message right after showing the menu (see
    // TrayApp.ShowContextMenuAt's AtControl branch — DO NOT remove that call).
    public const uint WM_NULL = 0x0000;

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
    public static extern bool IsWindow(IntPtr hWnd);

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

    // ── Foreground-attach dance (shared: NotifyIconMenuHost tray-menu path + TrayApp's
    // overlay ContextMenuStrip path) ────────────────────────────────────────────────────────

    /// <summary>
    /// Diagnostic snapshot of a completed <see cref="InvokeWithForegroundAttached"/> call:
    /// the foreground HWND observed at entry, both thread ids, and whether
    /// <see cref="AttachThreadInput"/> actually ran (it is skipped, and reports false, when
    /// the calling thread already owns the foreground thread's input queue). Callers use this
    /// purely for Debug logging.
    /// </summary>
    public readonly record struct ForegroundAttachOutcome(
        IntPtr ForegroundWindow, uint ForegroundThreadId, uint CallingThreadId, bool Attached);

    /// <summary>
    /// Runs <paramref name="action"/> with the calling thread's input queue temporarily
    /// attached to the current foreground window's thread, so any <see cref="SetForegroundWindow"/>
    /// call made inside <paramref name="action"/> succeeds. Windows normally rejects
    /// SetForegroundWindow from a thread that isn't itself the foreground thread — this is the
    /// standard AttachThreadInput workaround, the same technique <see cref="ForceForeground"/>
    /// above uses (with its own independent, hand-rolled sequence — not migrated onto this
    /// helper). This method is the shared implementation for the two newer foreground-grant
    /// call sites — <c>NotifyIcon.ShowContextMenu</c> (reflected by <c>NotifyIconMenuHost</c>)
    /// and the overlay's <c>ContextMenuStrip</c> path (<c>TrayApp.ShowContextMenuAt</c>) — so
    /// those two don't each re-derive the same mechanics independently.
    /// Always detaches in a finally, even if <paramref name="action"/> throws.
    /// </summary>
    public static ForegroundAttachOutcome InvokeWithForegroundAttached(Action action)
    {
        var fg = GetForegroundWindow();
        var fgThread = fg == IntPtr.Zero ? 0u : GetWindowThreadProcessId(fg, out _);
        var myThread = GetCurrentThreadId();
        var attached = false;
        if (fgThread != 0 && fgThread != myThread)
            attached = AttachThreadInput(fgThread, myThread, true);
        try
        {
            action();
        }
        finally
        {
            if (attached) AttachThreadInput(fgThread, myThread, false);
        }
        return new ForegroundAttachOutcome(fg, fgThread, myThread, attached);
    }

    // ── Foreground-restore capture-time validation ──────────────────────────────────────────
    // Rejects a GetForegroundWindow() capture that cannot be a legitimate restore target: not
    // a window at all, belonging to imrdy's own process (every transient ToolStripDropDown /
    // ContextMenuStrip popup this app itself creates — the primary observed failure mode,
    // where right-clicking while a previous menu is still closing captures that dying popup
    // instead of the user's real window), or lacking WS_CAPTION (a borderless popup even from
    // another process — tooltips, flyouts, other apps' context menus).

    private const int GWL_STYLE = -16;
    private const long WS_CAPTION = 0x00C00000L;

    // GetWindowLongPtrW is the real exported 64-bit entry point (on 32-bit Windows it is a
    // compile-time macro over GetWindowLongW with no such export) — safe to call directly
    // since imrdy only ships win-x64/win-arm64 builds (see release.yml).
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    /// <summary>
    /// True if <paramref name="hWnd"/> has the WS_CAPTION style — a heuristic distinguishing
    /// "real" top-level app windows (terminals, editors, browsers) from borderless popups
    /// (ToolStripDropDown menus, tooltips, flyouts), which never set it.
    /// </summary>
    public static bool HasCaptionStyle(IntPtr hWnd) =>
        (GetWindowLongPtr(hWnd, GWL_STYLE).ToInt64() & WS_CAPTION) == WS_CAPTION;

    /// <summary>
    /// Pure decision logic for whether a captured foreground-window candidate is a legitimate
    /// restore target. Factored out from the live Win32 queries (<see cref="IsWindow"/>,
    /// <see cref="GetWindowThreadProcessId"/>, <see cref="HasCaptionStyle"/>) above so it is
    /// unit-testable without a real window handle.
    /// </summary>
    public static bool IsAcceptableForegroundCandidate(bool isValidWindow, bool isOwnProcess, bool hasCaption)
        => isValidWindow && !isOwnProcess && hasCaption;
}
