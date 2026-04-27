using System.Runtime.InteropServices;

namespace Imrdy.Integration.Tests.Helpers;

/// <summary>
/// P/Invoke helpers for locating a visible window belonging to a spawned process.
/// Shared by PreviewDashboardCommandTests and PreviewAllFixturesTests.
/// </summary>
public static class WindowHelper
{
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    public const uint WM_CLOSE = 0x0010;

    public static nint FindVisibleWindowForProcess(uint pid)
    {
        nint found = nint.Zero;

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd))
                return true;

            GetWindowThreadProcessId(hWnd, out var windowPid);
            if (windowPid == pid)
            {
                found = hWnd;
                return false;
            }

            return true;
        }, nint.Zero);

        return found;
    }
}
