using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Imrdy.Windows.Menus;

/// <summary>
/// Shows a <see cref="NotifyIcon"/>'s context menu using the same private code path
/// Windows invokes for native tray right-clicks. Encapsulates two well-known Win32
/// idioms that together make menus work reliably from a WS_EX_NOACTIVATE source
/// (like our overlay):
///
///   1. Reflection-invoked NotifyIcon.ShowContextMenu — the private method performs
///      the foreground-window dance, positions the menu above or below the icon
///      based on taskbar edge, and drives ToolStripManager.ModalMenuFilter cleanly.
///      ContextMenuStrip.Show bypasses all of this and leaves the filter stuck so
///      subsequent right-clicks silently no-op.
///
///   2. AttachThreadInput — SetForegroundWindow inside ShowContextMenu silently
///      fails when the calling thread doesn't own foreground input (which is the
///      case when our WS_EX_NOACTIVATE overlay is the source of the right-click).
///      Attaching the overlay's thread input to the current foreground thread
///      temporarily lets SetForegroundWindow succeed; we detach immediately after.
///      Without this, the FIRST right-click on an unfocused overlay silently
///      gives focus without showing a menu.
/// </summary>
internal static class NotifyIconMenuHost
{
    private static readonly MethodInfo? s_showContextMenu =
        typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>
    /// True if the private NotifyIcon.ShowContextMenu method was found by reflection.
    /// When false, Show() falls back to ContextMenuStrip.Show(Cursor.Position) which
    /// skips the foreground-window dance and often results in an instantly-closing menu.
    /// </summary>
    public static bool ReflectionAvailable => s_showContextMenu is not null;

    /// <summary>
    /// Diagnostic record of the most recent Show invocation: fg HWND, threads, and whether
    /// AttachThreadInput succeeded. Read by TrayApp logging after each Show call.
    /// </summary>
    public static (IntPtr Fg, uint FgThread, uint MyThread, bool Attached) LastInvoke { get; private set; }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    /// <summary>
    /// Shows <paramref name="icon"/>'s ContextMenuStrip anchored next to the tray icon.
    /// Falls back to ContextMenuStrip.Show at cursor position if the private method is
    /// unavailable (e.g., future .NET breaks reflection access).
    /// </summary>
    public static void Show(NotifyIcon icon)
    {
        if (s_showContextMenu is not null)
        {
            InvokeWithForegroundAttached(() => s_showContextMenu.Invoke(icon, null));
            return;
        }

        // Fallback: less reliable but keeps the app functional if reflection breaks.
        icon.ContextMenuStrip?.Show(Cursor.Position);
    }

    /// <summary>
    /// Invokes <paramref name="show"/> with this thread temporarily attached to the
    /// current foreground window's input queue, so any SetForegroundWindow call inside
    /// <paramref name="show"/> succeeds.
    /// </summary>
    private static void InvokeWithForegroundAttached(Action show)
    {
        var fg = GetForegroundWindow();
        var fgThread = fg == IntPtr.Zero ? 0u : GetWindowThreadProcessId(fg, out _);
        var myThread = GetCurrentThreadId();
        var attached = false;
        if (fgThread != 0 && fgThread != myThread)
            attached = AttachThreadInput(fgThread, myThread, true);
        LastInvoke = (fg, fgThread, myThread, attached);
        try
        {
            show();
        }
        finally
        {
            if (attached) AttachThreadInput(fgThread, myThread, false);
        }
    }
}
