namespace Imrdy.Core.Desktop;

/// <summary>
/// Abstraction for virtual desktop management.
/// Enables desktop switching, window focusing, and desktop enumeration.
/// Implementations must handle graceful degradation when COM is unavailable.
/// </summary>
public interface IDesktopManager : IDisposable
{
    /// <summary>
    /// Whether virtual desktop COM interfaces are available on this OS build.
    /// False on unknown builds or after COM failure.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Gets the index of the currently active virtual desktop.
    /// Returns null if unavailable or COM fails.
    /// </summary>
    int? GetCurrentDesktopIndex();

    /// <summary>
    /// Gets the virtual desktop index for the given window handle.
    /// Returns null if unavailable, the window doesn't exist, or COM fails.
    /// </summary>
    int? GetDesktopForWindow(IntPtr hwnd);

    /// <summary>
    /// Switches to the virtual desktop at the given index.
    /// No-op if unavailable or the index is out of range.
    /// </summary>
    void SwitchToDesktop(int index);

    /// <summary>
    /// Brings the specified window to the foreground, switching desktops if needed.
    /// Uses AttachThreadInput for robust foreground activation.
    /// No-op if unavailable.
    /// </summary>
    void FocusWindow(IntPtr hwnd);

    /// <summary>
    /// Gets the total number of virtual desktops.
    /// Returns null if unavailable or COM fails.
    /// </summary>
    int? GetDesktopCount();

    /// <summary>
    /// Recreates COM objects from scratch.
    /// Call after Explorer restart or COM failure detection.
    /// </summary>
    void Reinitialize();

    /// <summary>
    /// Pins a top-level window so it appears on all virtual desktops (the COM equivalent
    /// of the user's Task View right-click → "Show this window on all desktops"). Used by
    /// hover-preview windows like DashboardForm that aren't WS_EX_LAYERED shell-helpers —
    /// without pinning, non-layered top-level windows are bound to their creation desktop.
    /// Idempotent — safe to call multiple times for the same hwnd.
    /// No-op when unavailable, hwnd is IntPtr.Zero, or COM fails.
    /// </summary>
    void PinWindowToAllDesktops(IntPtr hwnd);
}
