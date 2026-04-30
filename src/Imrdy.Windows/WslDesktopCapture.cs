using Imrdy.Core.Desktop;
using Imrdy.Core.State;

namespace Imrdy.Windows;

/// <summary>
/// Pure static helper that stamps the current virtual desktop index onto a WSL
/// session state file when the tray picks up a SessionStart FSW event.
/// Extracted from TrayApp so the logic is unit-testable without WinForms instantiation.
/// </summary>
internal static class WslDesktopCapture
{
    /// <summary>
    /// If <paramref name="state"/> is a WSL SessionStart with no existing DesktopIndex,
    /// captures the current desktop via <paramref name="desktopManager"/> and writes the
    /// stamped state back to <paramref name="filePath"/> via <paramref name="writeFile"/>.
    /// Returns the (possibly updated) state.
    /// </summary>
    /// <remarks>
    /// WSL hooks have no COM access, so they cannot write desktop_index themselves.
    /// The Windows tray stamps the desktop the user is currently viewing when it first
    /// sees the SessionStart state file — which is the intended session desktop (D10).
    /// </remarks>
    internal static StateFileModel MaybeStampDesktopIndex(
        StateFileModel state,
        string filePath,
        IDesktopManager desktopManager,
        Action<string, StateFileModel> writeFile)
    {
        if (state.WslDistro is null
            || !string.Equals(state.HookEvent, "SessionStart", StringComparison.OrdinalIgnoreCase)
            || state.DesktopIndex is not null)
        {
            return state;
        }

        var index = desktopManager.GetCurrentDesktopIndex();
        if (!index.HasValue)
        {
            return state;
        }

        state = state with { DesktopIndex = index.Value };
        writeFile(filePath, state);
        return state;
    }
}
