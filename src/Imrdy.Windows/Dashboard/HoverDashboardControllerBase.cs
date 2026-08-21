using System.Drawing;
using System.Windows.Forms;
using Imrdy.Core.Desktop;
using Imrdy.Core.Display;
using Imrdy.Windows.Desktop;
using Imrdy.Windows.Overlay;
using Microsoft.Extensions.Logging;

namespace Imrdy.Windows.Dashboard;

/// <summary>
/// Abstract base for hover dashboard controllers. Owns the dwell/grace/dismissal state
/// machine, form lifecycle (show/hide/dispose), opacity animation, and diagnostic heartbeat.
///
/// Derived controllers override <see cref="TryHitTestForOurDomain"/>,
/// <see cref="BuildViewModel"/>, <see cref="CreateForm"/>, <see cref="ShowForm"/>, and
/// <see cref="ApplyViewModelUpdate"/> to plug in domain-specific hit-testing,
/// view-model construction, and form interaction.
///
/// Per P6 (imrdy-expert wiki), this base does NOT subscribe to
/// <see cref="OverlayPanel.SurfaceInteracted"/>. TrayApp owns all
/// <c>+=</c>/<c>-=</c> wiring and calls <see cref="HandleSurfaceInteraction"/> directly.
/// </summary>
internal abstract class HoverDashboardControllerBase : IDisposable
{
    // 200ms dwell threshold at 100ms tick rate
    private const int DwellThresholdTicks = 2;
    // 300ms grace corridor at 100ms tick rate
    private const int DismissThresholdTicks = 3;
    // 12px bridge gap — mirrors HoverDashboardFormBase.BridgeGap.
    private const int BridgeGap = 12;
    // 2-tick (200ms) grace traversing the HWND-less bridge gap. Must be < DismissThresholdTicks.
    private const int BridgeTraversalGraceTicks = 2;
    // Live-refresh cadence: rebuild VM every 10 ticks (~1s) while visible on same item.
    private const int RefreshIntervalTicks = 10;

    protected readonly OverlayPanel _overlayWindow;
    protected readonly IDesktopManager? _desktopManager;
    protected readonly ILoggerFactory _loggerFactory;
    protected readonly ILogger _logger;

    // Transient form — created fresh on each Show, disposed on each Hide.
    private HoverDashboardFormBase? _form;
    private bool _disposed;

    // Single canonical hover-tracking field — replaces the dual (_currentHoveredIndex,
    // _hoveredItemId) pattern. null when no item is hovered or form is hidden.
    private DisplayItem? _hoveredItem;

    // Dwell accumulator — ticks cursor continuously inside overlay Bounds.
    private int _dwellTicks;
    // Grace corridor accumulator — ticks cursor continuously outside union region.
    private int _outsideTicks;
    // Transition tracking — true when cursor was inside overlay on the previous tick.
    private bool _wasInOverlayLastTick;
    // Tracks whether cursor was geometrically inside actualBounds on the previous tick.
    private bool _wasGeometricallyInOverlayLastTick;
    // Post-interaction cooldown: suppresses dwell while cursor remains on overlay after click.
    private bool _awaitingOverlayExit;

    // Opacity animation state: 0 = idle, 1 = reveal in progress, -1 = dismiss in progress.
    private int _opacityDirection;

    // Tick counter for throttled live-refresh while dashboard is visible.
    private int _ticksSinceLastRefresh;

    // Diagnostic fields — no behavioral effect.
    private int _tickCount;
    private Rectangle _lastLoggedOverlayBounds;
    private bool _firstTick = true;

    /// <summary>
    /// Initializes base fields. Does NOT subscribe to
    /// <see cref="OverlayPanel.SurfaceInteracted"/> — TrayApp owns that wiring.
    /// </summary>
    /// <param name="overlayWindow">Required. The overlay panel to hit-test against.</param>
    /// <param name="desktopManager">
    /// May be null (headless callers pass null per D9). Stored and forwarded to
    /// derived <see cref="CreateForm"/> overrides so the form ctor receives it.
    /// </param>
    /// <param name="loggerFactory">Required.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="overlayWindow"/> or <paramref name="loggerFactory"/> is null.
    /// </exception>
    protected HoverDashboardControllerBase(
        OverlayPanel overlayWindow,
        IDesktopManager? desktopManager,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(overlayWindow);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _overlayWindow = overlayWindow;
        _desktopManager = desktopManager;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger(GetType());
    }

    // ---- Abstract contract ----

    /// <summary>
    /// Builds the view-model for the given display item.
    /// Returns null to suppress showing the form for this dwell tick (P7 suppression path).
    /// Receives the resolved <see cref="DisplayItem"/> directly — no re-lookup needed.
    /// </summary>
    protected abstract object? BuildViewModel(DisplayItem item);

    /// <summary>
    /// Creates a fresh <see cref="HoverDashboardFormBase"/>-derived form from the given
    /// view model. The base owns Show, Pin, Hide, Dispose. Derived casts
    /// <paramref name="viewModel"/> to its known VM type (P5 round-trip).
    /// </summary>
    protected abstract Form CreateForm(object viewModel);

    /// <summary>
    /// Hit-tests at the given client X coordinate and returns true when a
    /// <see cref="DisplayItem"/> belonging to this controller's domain is at that position.
    /// Derived calls <see cref="OverlayPanel.TryHitTestAtClient"/> and filters
    /// by <see cref="DisplayItem.ItemType"/>.
    /// </summary>
    /// <param name="clientX">Client X coordinate (screen→client conversion done by base).</param>
    /// <param name="item">Resolved item when returning true; null otherwise.</param>
    /// <param name="hitIndex">Slot index when returning true; -1 otherwise.</param>
    protected abstract bool TryHitTestForOurDomain(int clientX, out DisplayItem? item, out int hitIndex);

    /// <summary>
    /// Shows the form with the given view model. Called after placement and opacity setup.
    /// Derived overrides call the typed <c>form.Show(TViewModel)</c> overload.
    /// </summary>
    protected abstract void ShowForm(HoverDashboardFormBase form, object viewModel);

    /// <summary>
    /// Applies an updated view model to the visible form in-place (no recreation).
    /// Called from Path B switch-detection. Derived overrides call the typed
    /// <c>form.Update(TViewModel)</c> overload.
    /// </summary>
    protected abstract void ApplyViewModelUpdate(HoverDashboardFormBase form, object viewModel);

    // ---- Extension points ----

    /// <summary>
    /// Called on each throttled live-refresh tick while the form is visible and the cursor
    /// remains on the same item. Base default is no-op.
    /// <see cref="SessionHoverDashboardController"/> overrides to call
    /// <c>RebuildAndApplyUpdate(currentItem.Id)</c>.
    /// </summary>
    protected virtual void OnSameItemRefreshTick(DisplayItem currentItem) { }

    /// <summary>
    /// Called after the form is shown and pinned. Override to perform domain-specific
    /// post-show work (e.g., kick off async git fetch).
    /// </summary>
    protected virtual void OnFormShown(DisplayItem item, object viewModel, Point cursor) { }

    /// <summary>
    /// Called when the form is hidden or force-hidden (both <c>HideForm</c> and
    /// <c>ForceHideForm</c> paths). Override to clear derived session-tracking state
    /// (e.g., reset <c>_hoveredSessionId</c> so stale async continuations are cancelled).
    /// Base default is no-op.
    /// </summary>
    protected virtual void OnFormHidden() { }

    // ---- Public API ----

    /// <summary>
    /// Raised after the form is successfully shown, pinned, and <see cref="OnFormShown"/>
    /// returns. The peer controller subscribes to this event and calls
    /// <see cref="HideIfVisible"/> so only one dashboard is visible at any moment.
    ///
    /// Wiring lives in TrayApp per P6 (anti-pattern: controllers do NOT subscribe to
    /// peers from within the base ctor or derived ctors).
    /// </summary>
    public event Action? FormShown;

    /// <summary>
    /// Hides the currently-visible dashboard with the existing fade-out animation.
    /// Idempotent: no-op when no form is shown or when the form is already dismissing
    /// (<c>_opacityDirection == -1</c>). Called by the peer controller's
    /// <see cref="FormShown"/> subscription so only one dashboard is visible at a time.
    ///
    /// Wiring lives in TrayApp per P6; this method is the subscriber endpoint.
    /// </summary>
    public void HideIfVisible()
    {
        if (_form is null || _form.IsDisposed || !_form.Visible) return;
        if (_opacityDirection == -1) return; // already dismissing
        _logger.LogDebug("HoverCtrlBase: HideIfVisible — peer shown, dismissing this form");
        if (_form.IsPinned) _form.Unpin();
        HideForm();
    }

    /// <summary>
    /// Called by TrayApp via two subscriptions: <c>OverlayPanel.SurfaceInteracted +=</c>
    /// after a left-click activates a session or workspace, and
    /// <c>OverlayPanel.DragCompleted +=</c> (through <c>HandleOverlayDragCompleted</c>) after
    /// a grip-drag completes. Right-click does NOT invoke this method — see the rationale on
    /// <see cref="OverlayPanel.SurfaceInteracted"/> for why firing it from the right-click
    /// branch would kill the ContextMenuStrip it is meant to coexist with. Dismisses the form
    /// immediately and sets the post-interaction cooldown so a phantom re-show is suppressed
    /// while the cursor stays on the overlay. Idempotent — safe even when no form is
    /// currently shown.
    /// </summary>
    public void HandleSurfaceInteraction()
    {
        _logger.LogDebug("HoverCtrlBase: HandleSurfaceInteraction received");
        ForceHideForm();
        _awaitingOverlayExit = true;
        _logger.LogDebug("HoverCtrlBase: post-interaction cooldown set");
    }

    /// <summary>
    /// Called by TrayApp on every 100ms drain-timer tick (UI thread). Drives the
    /// dwell-detection and grace-corridor-dismissal state machine.
    /// Reads <see cref="System.Windows.Forms.Cursor.Position"/> internally.
    /// </summary>
    public void OnDrainTick()
    {
        if (_firstTick)
        {
            _firstTick = false;
            var ftCursor = Cursor.Position;
            var ftBounds = _overlayWindow.Bounds;
            _logger.LogDebug(
                "HoverCtrlBase: first-tick fired cursor={CX},{CY} overlayBounds={BX},{BY},{BW},{BH}",
                ftCursor.X, ftCursor.Y,
                ftBounds.X, ftBounds.Y, ftBounds.Width, ftBounds.Height);
        }

        _tickCount++;

        // Guard stale form reference (disposed externally or from a prior tick).
        if (_form is not null && _form.IsDisposed)
        {
            _logger.LogDebug("HoverCtrlBase: stale-form-disposed guard fired");
            _form = null;
            _hoveredItem = null;
        }

        if (_disposed) return;

        // Overlay hidden (e.g. tray menu open) — suppress dwell, dismiss any visible form.
        if (!_overlayWindow.Visible)
        {
            _dwellTicks = 0;
            _wasInOverlayLastTick = false;
            if (_form is not null && _form.Visible)
            {
                if (_form.IsPinned) _form.Unpin();
                HideForm();
            }
            return;
        }

        var cursor = Cursor.Position;
        var overlayBounds = _overlayWindow.Bounds;

        // Z-order hit-test: geometric containment alone is insufficient.
        var overlayHwnd = _overlayWindow.IsHandleCreated ? _overlayWindow.Handle : IntPtr.Zero;
        var formHwnd = _form is { IsHandleCreated: true, IsDisposed: false } ? _form.Handle : IntPtr.Zero;
        var topHwndAtCursor = PInvokeOverlay.WindowAtPoint(cursor);
        var cursorOverOverlay = overlayBounds.Contains(cursor) && topHwndAtCursor == overlayHwnd;
        var cursorOverForm = formHwnd != IntPtr.Zero && topHwndAtCursor == formHwnd;
        var cursorOverEither = cursorOverOverlay || cursorOverForm;

        // Diagnostic heartbeat every 10th tick (~1s).
        if (_tickCount % 10 == 0)
        {
            var formBounds = (_form is not null && !_form.IsDisposed) ? _form.Bounds : Rectangle.Empty;
            _logger.LogDebug(
                "HoverCtrlBase: tick-state cursor={CursorX},{CursorY} actualBounds={ABX},{ABY},{ABW},{ABH} topHwnd={TopHwnd:X} overlayHwnd={OverlayHwnd:X} formHwnd={FormHwnd:X} cursorOverOverlay={COO} cursorOverForm={COF} _awaitingOverlayExit={AwaitingExit} _dwellTicks={DwellTicks} _wasInOverlayLastTick={WasInOverlay} formVisible={FormVisible} formBounds={FBX},{FBY},{FBW},{FBH} hoveredItemId={HoveredId}",
                cursor.X, cursor.Y,
                overlayBounds.X, overlayBounds.Y, overlayBounds.Width, overlayBounds.Height,
                topHwndAtCursor.ToInt64(), overlayHwnd.ToInt64(), formHwnd.ToInt64(),
                cursorOverOverlay, cursorOverForm,
                _awaitingOverlayExit, _dwellTicks, _wasInOverlayLastTick,
                _form?.Visible ?? false,
                formBounds.X, formBounds.Y, formBounds.Width, formBounds.Height,
                _hoveredItem?.Id);
        }

        // Diagnostic: cursor geometrically inside overlay but another window on top.
        if (overlayBounds.Contains(cursor) && !cursorOverOverlay && _wasGeometricallyInOverlayLastTick)
            _logger.LogDebug(
                "HoverCtrlBase: cursor geometrically in overlay but topHwnd={TopHwnd:X} != overlayHwnd={OverlayHwnd:X} — overlay covered",
                topHwndAtCursor.ToInt64(), overlayHwnd.ToInt64());

        // Log actual screen bounds whenever they change.
        if (overlayBounds != _lastLoggedOverlayBounds)
        {
            _logger.LogDebug(
                "HoverCtrlBase: actualScreenBounds changed to {X},{Y},{W},{H}",
                overlayBounds.X, overlayBounds.Y, overlayBounds.Width, overlayBounds.Height);
            _lastLoggedOverlayBounds = overlayBounds;
        }

        // ---- Path B: form visible ----
        if (_form is not null && _form.Visible)
        {
            _logger.LogDebug(
                "HoverCtrlBase: formVisible-branch-enter cursor={CX},{CY} formBounds={FX},{FY},{FW},{FH} hoveredItemId={HoveredId} _awaitingOverlayExit={AwaitExit} _opacityDirection={OpDir}",
                cursor.X, cursor.Y,
                _form.Bounds.X, _form.Bounds.Y, _form.Bounds.Width, _form.Bounds.Height,
                _hoveredItem?.Id, _awaitingOverlayExit, _opacityDirection);

            var unionBounds = ComputeUnionWithBridge(overlayBounds, _form.Bounds);
            if (cursorOverEither)
            {
                if (_outsideTicks > 0) _outsideTicks = 0;

                if (cursorOverOverlay)
                {
                    // Convert screen→client before hit-testing.
                    int cx = cursor.X, cy = cursor.Y;
                    PInvokeOverlay.ScreenToClientPoint(_overlayWindow.Handle, ref cx, ref cy);

                    if (TryHitTestForOurDomain(cx, out var newItem, out _)
                        && newItem is not null
                        && newItem.Id != _hoveredItem?.Id)
                    {
                        _logger.LogDebug("HoverCtrlBase: switch-detected from={From} to={To} rebuilding-vm",
                            _hoveredItem?.Id, newItem.Id);
                        _hoveredItem = newItem;
                        var newVm = BuildViewModel(newItem);
                        if (newVm is not null)
                            ApplyViewModelUpdate(_form, newVm);
                        _ticksSinceLastRefresh = 0;
                    }
                }
            }
            else if (unionBounds.Contains(cursor) && _outsideTicks < BridgeTraversalGraceTicks)
            {
                // Bridge gap — hold steady, do not increment _outsideTicks.
            }
            else
            {
                _outsideTicks++;
                if (_outsideTicks == 1)
                    _logger.LogDebug("HoverCtrlBase: cursor left union region; _outsideTicks=1");
                if (_outsideTicks >= DismissThresholdTicks)
                {
                    _logger.LogDebug("HoverCtrlBase: dismiss threshold reached (_outsideTicks={OutsideTicks}), calling HideForm", _outsideTicks);
                    if (_form.IsPinned) _form.Unpin();
                    HideForm();
                }
            }

            // Throttled live refresh.
            if (_hoveredItem is not null && _opacityDirection != -1)
            {
                _ticksSinceLastRefresh++;
                if (_ticksSinceLastRefresh >= RefreshIntervalTicks)
                {
                    _ticksSinceLastRefresh = 0;
                    _logger.LogDebug("HoverCtrlBase: throttled-refresh itemId={ItemId}", _hoveredItem.Id);
                    OnSameItemRefreshTick(_hoveredItem);
                }
            }

            _wasInOverlayLastTick = cursorOverOverlay;
            _wasGeometricallyInOverlayLastTick = overlayBounds.Contains(cursor);
            _dwellTicks = 0;

            StepOpacity();
            return;
        }

        // ---- Path A: form hidden ----
        var cursorInOverlay = cursorOverOverlay;

        if (_awaitingOverlayExit)
        {
            _logger.LogDebug(
                "HoverCtrlBase: formHidden-branch (_awaitingOverlayExit=true) cursor={CX},{CY} cursorInOverlay={CursorIn}",
                cursor.X, cursor.Y, cursorInOverlay);
            if (!cursorInOverlay)
            {
                _awaitingOverlayExit = false;
                _logger.LogDebug("HoverCtrlBase: post-interaction cooldown lifted");
            }
            _dwellTicks = 0;
            _wasInOverlayLastTick = cursorInOverlay;
            _wasGeometricallyInOverlayLastTick = overlayBounds.Contains(cursor);
            return;
        }

        if (cursorInOverlay)
        {
            if (!_wasInOverlayLastTick)
                _logger.LogDebug("HoverCtrlBase: cursor entered overlay bounds; starting dwell");

            var prevDwellTicks = _dwellTicks;
            _dwellTicks++;
            if (prevDwellTicks == 0)
                _logger.LogDebug("HoverCtrlBase: dwell-began cursor={CX},{CY}", cursor.X, cursor.Y);
            if (_dwellTicks >= DwellThresholdTicks)
            {
                _logger.LogDebug("HoverCtrlBase: dwell threshold reached at {Cursor}, calling TryShowForm", cursor);
                TryShowForm(cursor);
                _dwellTicks = 0;
            }
        }
        else
        {
            if (_wasInOverlayLastTick)
                _logger.LogDebug("HoverCtrlBase: cursor exited overlay bounds (no form visible)");
            _dwellTicks = 0;
        }

        _wasInOverlayLastTick = cursorInOverlay;
        _wasGeometricallyInOverlayLastTick = overlayBounds.Contains(cursor);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _logger.LogDebug("HoverCtrlBase: Dispose called");
        // Base does NOT touch _overlayWindow.SurfaceInteracted — TrayApp owns -= per P6.
        ForceHideForm();
    }

    // ---- Protected helpers for derived classes ----

    /// <summary>
    /// Applies a view-model update to the currently-visible form (if any) by delegating
    /// to <see cref="ApplyViewModelUpdate"/>. No-op when no form is currently shown or
    /// when the form has been disposed. Called by derived classes from their typed
    /// rebuild helpers (e.g., <c>RebuildAndApplyUpdate</c>).
    /// </summary>
    protected void UpdateCurrentForm(object viewModel)
    {
        if (_form is null || _form.IsDisposed || !_form.Visible) return;
        ApplyViewModelUpdate(_form, viewModel);
    }

    // ---- Private helpers ----

    private void TryShowForm(Point cursor)
    {
        _logger.LogDebug("HoverCtrlBase: TryShowForm at cursor={CX},{CY}", cursor.X, cursor.Y);

        // Screen→client conversion before hit-testing.
        int cx = cursor.X, cy = cursor.Y;
        PInvokeOverlay.ScreenToClientPoint(_overlayWindow.Handle, ref cx, ref cy);

        if (!TryHitTestForOurDomain(cx, out var item, out _) || item is null)
        {
            _logger.LogDebug("HoverCtrlBase: TryShowForm aborted — no domain item under cursor");
            return;
        }

        var viewModel = BuildViewModel(item);
        if (viewModel is null)
        {
            _logger.LogDebug("HoverCtrlBase: TryShowForm aborted — BuildViewModel returned null for item={ItemId}", item.Id);
            return;
        }

        _logger.LogDebug("HoverCtrlBase: TryShowForm item={ItemId}, recreating form", item.Id);

        DisposeForm();

        _hoveredItem = item;
        _form = (HoverDashboardFormBase)CreateForm(viewModel);

        var workingArea = Screen.FromControl(_overlayWindow).WorkingArea;
        var overlayBounds = _overlayWindow.Bounds;

        var (anchorMode, anchorX, anchorY) = _form.ComputeAnchorPlacement(overlayBounds, cursor, workingArea);

        _logger.LogDebug(
            "HoverCtrlBase: form-place-decision overlay={OX},{OY},{OW},{OH} cursor={CX},{CY} formHeight={FH} formWidth={FW} → anchorMode={Mode} anchorX={AX} anchorY={AY}",
            overlayBounds.X, overlayBounds.Y, overlayBounds.Width, overlayBounds.Height,
            cursor.X, cursor.Y, _form.Height, _form.Width,
            anchorMode, anchorX, anchorY);

        _form.PlaceWithAnchor(anchorX, anchorY, anchorMode);
        _form.Opacity = 0.0;
        _opacityDirection = 1;
        _logger.LogDebug("HoverCtrlBase: opacity-reveal-started");

        ShowForm(_form, viewModel);

        _logger.LogDebug(
            "HoverCtrlBase: form shown at Location={Location} Bounds={Bounds} hwnd={Handle}",
            _form.Location, _form.Bounds, _form.Handle);

        _ticksSinceLastRefresh = 0;
        _form.PinAcrossVirtualDesktops();
        _logger.LogDebug("HoverCtrlBase: form pinned to all desktops");
        _outsideTicks = 0;

        OnFormShown(item, viewModel, cursor);
        FormShown?.Invoke();
    }

    private void HideForm()
    {
        _logger.LogDebug("HoverCtrlBase: HideForm called — starting dismiss animation");
        if (_form is null || _form.IsDisposed)
        {
            _hoveredItem = null;
            _outsideTicks = 0;
            _dwellTicks = 0;
            OnFormHidden();
            return;
        }

        if (_opacityDirection == -1) return; // already dismissing

        _opacityDirection = -1;
        _logger.LogDebug("HoverCtrlBase: opacity-dismiss-started");
        _hoveredItem = null;
        _outsideTicks = 0;
        _dwellTicks = 0;
        OnFormHidden();

        if (_form.IsPinned) _form.Unpin();
    }

    private void ForceHideForm()
    {
        _logger.LogDebug("HoverCtrlBase: ForceHideForm called");
        DisposeForm();
        _hoveredItem = null;
        _outsideTicks = 0;
        _dwellTicks = 0;
        OnFormHidden();
    }

    private void DisposeForm()
    {
        if (_form is null) return;
        if (!_form.IsDisposed)
        {
            _form.Close();
            _form.Dispose();
        }
        _form = null;
        _opacityDirection = 0;
    }

    private void StepOpacity()
    {
        if (_form is null || _opacityDirection == 0) return;

        if (_opacityDirection == 1)
        {
            var next = _form.Opacity + 0.5;
            if (next >= 1.0)
            {
                _form.Opacity = 1.0;
                _opacityDirection = 0;
                _logger.LogDebug("HoverCtrlBase: reveal complete (Opacity=1.0)");
            }
            else
            {
                _form.Opacity = next;
            }
        }
        else if (_opacityDirection == -1)
        {
            var next = _form.Opacity - 0.5;
            if (next <= 0.0)
            {
                _form.Opacity = 0.0;
                _opacityDirection = 0;
                _logger.LogDebug("HoverCtrlBase: dismiss complete (Opacity=0.0), disposing form");
                DisposeForm();
            }
            else
            {
                _form.Opacity = next;
            }
        }
    }

    private static Rectangle ComputeUnionWithBridge(Rectangle overlayBounds, Rectangle formBounds)
    {
        var union = Rectangle.Union(overlayBounds, formBounds);
        return Rectangle.Inflate(union, BridgeGap, BridgeGap);
    }
}
