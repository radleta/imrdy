using System.Reflection;
using System.Windows.Forms;
using FluentAssertions;
using Imrdy.Core;
using Imrdy.Core.Display;
using Imrdy.Core.Graphics;
using Imrdy.Windows.Interaction;
using Imrdy.Windows.Overlay;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Imrdy.Windows.Tests.Overlay;

/// <summary>
/// Unit tests for <see cref="InteractiveOverlayWindow"/> mouse-event behavior.
///
/// State matrix under test (axes: event × hit-region × IsDashboardHoverActive):
///   Left MouseDown over icon   → SurfaceInteracted fires, router dispatched
///   Left MouseDown in gap (dashboard active) → SurfaceInteracted fires, router NOT dispatched
///   Right MouseUp over icon (dashboard active) → SurfaceInteracted fires, menu dispatched
///   Right MouseUp in gap (dashboard active)    → SurfaceInteracted fires, menu NOT dispatched
///   Middle MouseDown (any)     → SurfaceInteracted does NOT fire
///
/// Test seam: <see cref="OnMouseDown"/> and <see cref="OnMouseUp"/> are protected — we
/// invoke them via <see cref="MethodInfo"/> reflection so no production code is modified.
/// <see cref="_items"/> (protected in <see cref="OverlayWindowBase"/>) is set by reflection.
/// </summary>
public class InteractiveOverlayWindowTests : IDisposable
{
    // OverlayConfig: Size=32, Spacing=4 → slot=36
    // Icon hit:  X=10  → i=0,  inSlot=10 < 32  → hit  (index 0)
    // Gap click: X=33  → i=0,  inSlot=33 >= 32 → gap (returns false)
    private const int IconSize = 32;
    private const int IconSpacing = 4;
    private const int IconHitX = 10;   // well inside icon 0
    private const int GapX = 33;       // in the spacing region after icon 0

    private readonly OverlayConfig _config = new() { Size = IconSize, Spacing = IconSpacing };
    private readonly StubRouter _router = new();
    private readonly InteractiveOverlayWindow _window;

    // Reflection accessors
    private static readonly MethodInfo _onMouseDown =
        typeof(InteractiveOverlayWindow).GetMethod(
            "OnMouseDown",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            [typeof(MouseEventArgs)],
            null)!;

    private static readonly MethodInfo _onMouseUp =
        typeof(InteractiveOverlayWindow).GetMethod(
            "OnMouseUp",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            [typeof(MouseEventArgs)],
            null)!;

    // _items lives in OverlayWindowBase as a protected field
    private static readonly FieldInfo _itemsField =
        typeof(OverlayWindowBase).GetField(
            "_items",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

    public InteractiveOverlayWindowTests()
    {
        _window = new InteractiveOverlayWindow(
            _config,
            _router,
            NullLoggerFactory.Instance,
            new GraphicsPackLoader());
    }

    public void Dispose() => _window.Dispose();

    // --- Helpers ---

    private void SetItems(IReadOnlyList<DisplayItem> items) =>
        _itemsField.SetValue(_window, items);

    private void RaiseMouseDown(MouseEventArgs e) =>
        _onMouseDown.Invoke(_window, [e]);

    private void RaiseMouseUp(MouseEventArgs e) =>
        _onMouseUp.Invoke(_window, [e]);

    private static MouseEventArgs LeftDown(int x) =>
        new(MouseButtons.Left, 1, x, 0, 0);

    private static MouseEventArgs RightUp(int x) =>
        new(MouseButtons.Right, 1, x, 0, 0);

    private static MouseEventArgs MiddleDown(int x) =>
        new(MouseButtons.Middle, 1, x, 0, 0);

    private static DisplayItem MakeSessionItem() => new(
        Id: "session-1",
        ItemType: DisplayItemType.Session,
        Status: "idle",
        DesktopIndex: null,
        IconStyle: "circles",
        AgingTier: 0,
        IsVisible: true,
        Label: "S");

    // --- Tests ---

    /// <summary>
    /// Matrix rows 1+2: Left click over icon (dashboard active or not) → SurfaceInteracted fires,
    /// router dispatches ActivateSession.
    /// </summary>
    [Fact]
    public void OnMouseDown_LeftClickOverIcon_FiresSurfaceInteracted()
    {
        SetItems([MakeSessionItem()]);

        var fired = 0;
        _window.SurfaceInteracted += () => fired++;

        RaiseMouseDown(LeftDown(IconHitX));

        fired.Should().Be(1, "SurfaceInteracted must fire for any left-click, including over an icon");
        _router.ActivateSessionCallCount.Should().Be(1, "router dispatches on icon hit");
    }

    /// <summary>
    /// Matrix row 4 (bug-fix case): Left click in gap while dashboard is active →
    /// SurfaceInteracted fires, router NOT dispatched.
    /// </summary>
    [Fact]
    public void OnMouseDown_LeftClickInGap_DashboardActive_FiresSurfaceInteracted_NoRouterDispatch()
    {
        SetItems([MakeSessionItem()]);
        _window.IsDashboardHoverActive = true;

        var fired = 0;
        _window.SurfaceInteracted += () => fired++;

        RaiseMouseDown(LeftDown(GapX));

        fired.Should().Be(1, "SurfaceInteracted must fire even when the click misses an icon");
        _router.ActivateSessionCallCount.Should().Be(0, "gap click must not dispatch to router");
        _router.ActivateWorkspaceCallCount.Should().Be(0, "gap click must not dispatch to router");
    }

    /// <summary>
    /// Matrix row 8: Right click over icon while dashboard is active →
    /// SurfaceInteracted fires, menu dispatched.
    /// </summary>
    [Fact]
    public void OnMouseUp_RightClickOverIcon_DashboardActive_FiresSurfaceInteracted()
    {
        SetItems([MakeSessionItem()]);
        _window.IsDashboardHoverActive = true;

        var fired = 0;
        _window.SurfaceInteracted += () => fired++;

        // Right-click menu dispatch calls AtControl(this, e.Location) which requires
        // a window handle. Wrapping in try/catch lets us verify the event fired before
        // the WinForms handle-creation side-effect surfaces. The catch is scoped to
        // known WinForms handle/COM exceptions so genuine infrastructure failures
        // (e.g., reflection errors) still surface as test failures.
        try { RaiseMouseUp(RightUp(IconHitX)); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        { /* WinForms handle creation may throw in headless; event is already captured */ }

        fired.Should().Be(1, "SurfaceInteracted must fire for right-click over an icon when dashboard is active");
    }

    /// <summary>
    /// Matrix row 10 (bug-fix case): Right click in gap while dashboard is active →
    /// SurfaceInteracted fires, menu NOT dispatched.
    /// </summary>
    [Fact]
    public void OnMouseUp_RightClickInGap_DashboardActive_FiresSurfaceInteracted_NoMenu()
    {
        SetItems([MakeSessionItem()]);
        _window.IsDashboardHoverActive = true;

        var fired = 0;
        _window.SurfaceInteracted += () => fired++;

        RaiseMouseUp(RightUp(GapX));

        fired.Should().Be(1, "SurfaceInteracted must fire for right-click in a gap when dashboard is active");
        _router.OpenSessionMenuCallCount.Should().Be(0, "gap right-click must not open a menu");
        _router.OpenWorkspaceMenuCallCount.Should().Be(0, "gap right-click must not open a menu");
    }

    /// <summary>
    /// Matrix row 11 (regression prevention): Middle click never fires SurfaceInteracted.
    /// </summary>
    [Fact]
    public void OnMouseDown_MiddleClick_DoesNotFireSurfaceInteracted()
    {
        SetItems([MakeSessionItem()]);
        _window.IsDashboardHoverActive = true;

        var fired = 0;
        _window.SurfaceInteracted += () => fired++;

        RaiseMouseDown(MiddleDown(IconHitX));

        fired.Should().Be(0, "middle-click must never fire SurfaceInteracted");
    }

    // --- Stub router ---

    private sealed class StubRouter : ISessionInteractionRouter
    {
        public int ActivateSessionCallCount { get; private set; }
        public int ActivateWorkspaceCallCount { get; private set; }
        public int OpenSessionMenuCallCount { get; private set; }
        public int OpenWorkspaceMenuCallCount { get; private set; }

        public void ActivateSession(string sessionId) => ActivateSessionCallCount++;
        public void ActivateWorkspace(string workspacePath) => ActivateWorkspaceCallCount++;
        public void OpenSessionMenu(string sessionId, MenuAnchor anchor) => OpenSessionMenuCallCount++;
        public void OpenWorkspaceMenu(string workspacePath, MenuAnchor anchor) => OpenWorkspaceMenuCallCount++;
    }
}
