using System.Drawing;
using System.Windows.Forms;
using Imrdy.Core.Desktop;
using Imrdy.Core.Display;
using Imrdy.Core.Hooks;
using Imrdy.Windows.Desktop;
using Imrdy.Windows.Models;
using Imrdy.Windows.Overlay;
using Microsoft.Extensions.Logging;

namespace Imrdy.Windows.Dashboard;

/// <summary>
/// Manages the hover-triggered dashboard lifecycle on the overlay.
///
/// Registered as a tick callback on TrayApp's 100ms drain timer — no separate timer.
/// Dwell detection: 200ms (2 ticks) of Cursor.Position inside overlay Bounds triggers show.
/// Grace-corridor dismissal: 300ms (3 ticks) outside union(overlayBounds ∪ formBounds ∪ 12px bridge).
///
/// Flips <see cref="InteractiveOverlayWindow.IsDashboardHoverActive"/> on show/hide so
/// WM_NCHITTEST widens to the full icon row while the dashboard is visible.
///
/// Transient lifecycle: the DashboardForm is created fresh on each Show and disposed on each Hide.
/// After Show, <see cref="IDesktopManager.PinWindowToAllDesktops"/> is called so the form
/// appears on every virtual desktop. This is the COM equivalent of Task View right-click →
/// "Show this window on all desktops" (IVirtualDesktopPinnedApps::PinView). Idempotent.
/// Step 03 adds WM_MOUSEACTIVATE (MA_NOACTIVATE) and pin/unpin.
/// Steps 04/05 wire the HookAccumulationStore snapshot and real rendering.
/// </summary>
internal sealed class HoverDashboardController : IDisposable
{
    // 200ms dwell threshold at 100ms tick rate
    private const int DwellThresholdTicks = 2;
    // 300ms grace corridor at 100ms tick rate
    private const int DismissThresholdTicks = 3;
    // 12px bridge gap between overlay bottom and form top — cursor may briefly exit
    // the icon row while traveling to the form; this gap keeps the form visible.
    private const int BridgeGap = 12;
    // 2 tick (200ms) grace while traversing the HWND-less bridge gap between overlay and
    // dashboard form. WindowFromPoint returns a third window (or nothing) for those 12px,
    // but we don't want to start the dismiss counter. After ~200ms of no-HWND-hit we accept
    // that the cursor moved away. Must be < DismissThresholdTicks (3) so normal exits still
    // trigger dismissal once grace is consumed.
    private const int BridgeTraversalGraceTicks = 2;
    // Live-refresh cadence: rebuild the VM and apply it to the visible dashboard every
    // 10 ticks (~1s at 100ms tick rate) so hook events, turn counts, and state changes
    // are reflected while the cursor lingers on the same session icon.
    private const int RefreshIntervalTicks = 10;

    private readonly InteractiveOverlayWindow _overlayWindow;
    private readonly HookAccumulationStore _hookAccumulationStore;
    private readonly Func<IReadOnlyList<SessionEntry>> _sessionSource;
    private readonly IDesktopManager _desktopManager;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly GitInfoCache _gitCache; // injected from TrayApp (D5 ownership promotion)

    private DashboardForm? _form;
    private bool _disposed;

    // Dwell accumulator — ticks cursor has been continuously inside overlay Bounds
    private int _dwellTicks;
    // Session whose icon triggered the current show; null when form is hidden
    private string? _hoveredSessionId;
    // Grace corridor accumulator — ticks cursor has been continuously outside the union region
    private int _outsideTicks;
    // Transition tracking — true when cursor was inside overlay Bounds on the previous tick.
    // Used to fire "cursor entered overlay" log only on the first in-bound tick (not every tick).
    private bool _wasInOverlayLastTick;
    // Post-interaction cooldown: set true after HandleSurfaceInteraction to suppress dwell
    // accumulation while the cursor is still physically on the overlay row after a click.
    // Cleared when the cursor exits overlay bounds, allowing normal hover-to-show to resume.
    private bool _awaitingOverlayExit;

    // Opacity animation state:
    //   0 = no animation in progress
    //   1 = reveal in progress (stepping Opacity toward 1.0)
    //  -1 = dismiss in progress (stepping Opacity toward 0.0, then Hide())
    private int _opacityDirection;

    // Tick counter for throttled live-refresh while the dashboard is visible.
    private int _ticksSinceLastRefresh;

    // Diagnostic fields — no behavioral effect.
    // F3: tick counter for per-10th-tick heartbeat log.
    private int _tickCount;
    // F5: last actual screen bounds logged — logs a message whenever the value changes.
    // Tracks ActualScreenBounds (GetWindowRect) rather than Form.Bounds (which stays stale).
    private Rectangle _lastLoggedOverlayBounds;
    // First-tick flag: fires one log on the very first OnDrainTick call after construction.
    private bool _firstTick = true;
    // Tracks whether cursor was geometrically inside actualBounds on the previous tick.
    // Used to detect the "covered by another window" case: geometrically inside but
    // WindowAtPoint returns a different HWND. Set to actualBounds.Contains(cursor) at
    // end of each tick.
    private bool _wasGeometricallyInOverlayLastTick;

    public HoverDashboardController(
        InteractiveOverlayWindow overlayWindow,
        HookAccumulationStore hookAccumulationStore,
        Func<IReadOnlyList<SessionEntry>> sessionSource,
        IDesktopManager desktopManager,
        ILoggerFactory loggerFactory,
        GitInfoCache gitCache)
    {
        _overlayWindow = overlayWindow;
        _hookAccumulationStore = hookAccumulationStore;
        _sessionSource = sessionSource;
        _desktopManager = desktopManager;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<HoverDashboardController>();
        _gitCache = gitCache;
        _logger.LogDebug(
            "HoverCtrl: ctor complete sessionSource-ref-set overlay-window-ref={Handle}",
            overlayWindow.Handle);
    }

    /// <summary>
    /// Called on every 100ms drain-timer tick (UI thread). Implements dwell detection
    /// and grace-corridor dismissal.
    /// </summary>
    public void OnDrainTick(DateTimeOffset now)
    {
        // First-tick log: fires exactly once after construction to confirm the controller is active.
        if (_firstTick)
        {
            _firstTick = false;
            var ftCursor = Cursor.Position;
            var ftCachedBounds = _overlayWindow.Bounds;
            var ftActualBounds = _overlayWindow.ActualScreenBounds;
            var ftSessions = _sessionSource();
            _logger.LogDebug(
                "HoverCtrl: first-tick fired cursor={CX},{CY} cachedBounds={CBX},{CBY},{CBW},{CBH} actualBounds={ABX},{ABY},{ABW},{ABH} sessionsAvailable={Count}",
                ftCursor.X, ftCursor.Y,
                ftCachedBounds.X, ftCachedBounds.Y, ftCachedBounds.Width, ftCachedBounds.Height,
                ftActualBounds.X, ftActualBounds.Y, ftActualBounds.Width, ftActualBounds.Height,
                ftSessions.Count);
        }

        // F3: per-10th-tick diagnostic heartbeat — deferred until after z-order variables
        // are computed below so the log can include topHwnd / overlayHwnd / formHwnd.
        _tickCount++;

        // Guard stale form reference — can happen when Dispose() clears _form but a
        // queued tick fires before the drain handler returns.
        if (_form is not null && _form.IsDisposed)
        {
            _logger.LogDebug("HoverCtrl: stale-form-disposed guard fired; form was {WasNull}", "non-null");
            _form = null;
            _hoveredSessionId = null;
            _overlayWindow.IsDashboardHoverActive = false;
        }

        if (_disposed) return;

        // Overlay hidden — typically because a tray menu is open. Suppress dwell
        // accumulation and dismiss any visible dashboard. Cursor-in-bounds math below
        // uses Bounds which remains set while hidden, so the explicit Visible gate is
        // required.
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
        // Read ActualScreenBounds once per tick (each call is a P/Invoke). All subsequent
        // geometry checks in this tick use this local; never read _overlayWindow.Bounds
        // (WinForms' cached value stays stale on layered+toolwindow forms even after SetBounds).
        var overlayBounds = _overlayWindow.ActualScreenBounds;

        // Z-order hit-test: pure rectangle-containment is insufficient — other topmost windows
        // (taskbar popups, system shells, dragged windows, Win+Tab) can geometrically overlap
        // the overlay's screen rect while our overlay is not visually topmost at that point.
        // WindowFromPoint returns the HWND actually visible to the user at the given screen point.
        // We gate "cursor over overlay/form" on BOTH geometric containment AND z-order identity.
        var overlayHwnd = _overlayWindow.IsHandleCreated ? _overlayWindow.Handle : IntPtr.Zero;
        var formHwnd = _form is { IsHandleCreated: true, IsDisposed: false } ? _form.Handle : IntPtr.Zero;
        var topHwndAtCursor = PInvokeOverlay.WindowAtPoint(cursor);
        var cursorOverOverlay = overlayBounds.Contains(cursor) && topHwndAtCursor == overlayHwnd;
        var cursorOverForm = formHwnd != IntPtr.Zero && topHwndAtCursor == formHwnd;
        var cursorOverEither = cursorOverOverlay || cursorOverForm;

        // F3: per-10th-tick diagnostic heartbeat (~1 log/sec at 100ms tick rate).
        if (_tickCount % 10 == 0)
        {
            var formBounds = (_form is not null && !_form.IsDisposed) ? _form.Bounds : Rectangle.Empty;
            var actualBoundsForOverlap = overlayBounds; // already read above
            var formOverlapsOverlay = formBounds != Rectangle.Empty && formBounds.IntersectsWith(actualBoundsForOverlap);
            _logger.LogDebug(
                "HoverCtrl: tick-state cursor={CursorX},{CursorY} cachedBounds={CBX},{CBY},{CBW},{CBH} actualBounds={ABX},{ABY},{ABW},{ABH} topHwnd={TopHwnd:X} overlayHwnd={OverlayHwnd:X} formHwnd={FormHwnd:X} cursorOverOverlay={COO} cursorOverForm={COF} _awaitingOverlayExit={AwaitingExit} _dwellTicks={DwellTicks} _wasInOverlayLastTick={WasInOverlay} formVisible={FormVisible} formBounds={FBX},{FBY},{FBW},{FBH} formOverlapsOverlay={FOO}",
                cursor.X, cursor.Y,
                _overlayWindow.Bounds.X, _overlayWindow.Bounds.Y, _overlayWindow.Bounds.Width, _overlayWindow.Bounds.Height,
                overlayBounds.X, overlayBounds.Y, overlayBounds.Width, overlayBounds.Height,
                topHwndAtCursor.ToInt64(), overlayHwnd.ToInt64(), formHwnd.ToInt64(),
                cursorOverOverlay, cursorOverForm,
                _awaitingOverlayExit,
                _dwellTicks,
                _wasInOverlayLastTick,
                _form?.Visible ?? false,
                formBounds.X, formBounds.Y, formBounds.Width, formBounds.Height,
                formOverlapsOverlay);
        }

        // Diagnostic: cursor geometrically in overlay but another window on top — smoking gun
        // for z-order misfire that would have triggered a ghost dwell without this gate.
        if (overlayBounds.Contains(cursor) && !cursorOverOverlay && _wasGeometricallyInOverlayLastTick)
            _logger.LogDebug(
                "HoverCtrl: cursor geometrically in overlay but topHwnd={TopHwnd:X} != overlayHwnd={OverlayHwnd:X} — overlay covered by another window",
                topHwndAtCursor.ToInt64(), overlayHwnd.ToInt64());

        // F5: log actual screen bounds whenever they change from the last logged value.
        if (overlayBounds != _lastLoggedOverlayBounds)
        {
            _logger.LogDebug(
                "HoverCtrl: actualScreenBounds changed to {X},{Y},{W},{H}",
                overlayBounds.X, overlayBounds.Y, overlayBounds.Width, overlayBounds.Height);
            _lastLoggedOverlayBounds = overlayBounds;
        }

        if (_form is not null && _form.Visible)
        {
            _logger.LogDebug(
                "HoverCtrl: formVisible-branch-enter cursor={CX},{CY} formBounds={FX},{FY},{FW},{FH} overlayBounds={OX},{OY},{OW},{OH} hoveredSessionId={HoveredId} _awaitingOverlayExit={AwaitExit} _opacityDirection={OpDir}",
                cursor.X, cursor.Y,
                _form.Bounds.X, _form.Bounds.Y, _form.Bounds.Width, _form.Bounds.Height,
                overlayBounds.X, overlayBounds.Y, overlayBounds.Width, overlayBounds.Height,
                _hoveredSessionId,
                _awaitingOverlayExit,
                _opacityDirection);

            // Grace-corridor: use z-order hit-test as the primary signal. The geometric union
            // with BridgeGap serves as a SECONDARY anchor for the 12px HWND-less traversal band
            // between the overlay row bottom and the form top — neither HWND is under the cursor
            // in that gap, but we don't want to start the dismiss counter immediately.
            var unionBounds = ComputeUnionWithBridge(overlayBounds, _form.Bounds);
            if (cursorOverEither)
            {
                // Cursor is over one of our windows by z-order — reset dismissal counter.
                if (_outsideTicks > 0)
                    _outsideTicks = 0;

                // Live-session-switch: detect when the cursor has moved to a different
                // session icon while the form is still visible. Only check when z-order
                // confirms the overlay is actually under the cursor (not a covering window).
                // Rebuild data in-place — no form recreation, no re-pin, no opacity reset.
                if (cursorOverOverlay)
                {
                    _logger.LogDebug(
                        "HoverCtrl: switch-check cursor={CX},{CY} cursorOverOverlay=true currentHovered={HoveredId}",
                        cursor.X, cursor.Y, _hoveredSessionId);
                    var switchHit = _overlayWindow.TryGetSessionIdAtScreenPoint(cursor, out var nowHoveredId);
                    _logger.LogDebug(
                        "HoverCtrl: switch-check-result hoveredId={HovId} returnedTrue={Hit}",
                        nowHoveredId, switchHit);
                    if (switchHit && nowHoveredId != _hoveredSessionId)
                    {
                        _logger.LogDebug("HoverCtrl: switch-detected from={From} to={To} rebuilding-vm", _hoveredSessionId, nowHoveredId);
                        _hoveredSessionId = nowHoveredId;
                        var heightBeforeSwitch = _form?.Height ?? 0;
                        RebuildAndApplyUpdate(nowHoveredId);
                        var heightAfterSwitch = _form?.Height ?? 0;
                        _logger.LogDebug(
                            "HoverCtrl: switch-applied to={NewId} heightBefore={HB} heightAfter={HA} formBounds={FB}",
                            _hoveredSessionId, heightBeforeSwitch, heightAfterSwitch, _form?.Bounds ?? Rectangle.Empty);
                        _ticksSinceLastRefresh = 0; // restart cadence after session switch
                    }
                }
            }
            else if (unionBounds.Contains(cursor) && _outsideTicks < BridgeTraversalGraceTicks)
            {
                // Cursor is geometrically in the bridge gap (overlay bottom ↔ form top).
                // WindowFromPoint returns neither HWND here, but we grant up to
                // BridgeTraversalGraceTicks ticks before treating it as a genuine exit.
                // Do not reset _outsideTicks, do not increment — hold steady.
            }
            else
            {
                _outsideTicks++;
                if (_outsideTicks == 1)
                    _logger.LogDebug("HoverCtrl: cursor left union region; _outsideTicks=1");
                if (_outsideTicks >= DismissThresholdTicks)
                {
                    _logger.LogDebug("HoverCtrl: dismiss threshold reached (_outsideTicks={OutsideTicks}), calling HideForm", _outsideTicks);
                    if (_form.IsPinned)
                        _form.Unpin();
                    HideForm();
                }
            }

            // Throttled live refresh — rebuild the VM and apply it in place every
            // RefreshIntervalTicks (~1s) so the dashboard reflects fresh hook events,
            // session state, and turn counts while the cursor lingers on the same
            // session icon. Skipped while dismissing (Opacity stepping down).
            if (_hoveredSessionId is not null && _opacityDirection != -1)
            {
                _ticksSinceLastRefresh++;
                if (_ticksSinceLastRefresh >= RefreshIntervalTicks)
                {
                    _ticksSinceLastRefresh = 0;
                    _logger.LogDebug("HoverCtrl: throttled-refresh sessionId={SessionId}", _hoveredSessionId);
                    RebuildAndApplyUpdate(_hoveredSessionId);
                }
            }

            // Track overlay-in transition for the next entry into dwell branch
            _wasInOverlayLastTick = cursorOverOverlay;
            _wasGeometricallyInOverlayLastTick = overlayBounds.Contains(cursor);
            // Reset dwell counter while form is visible (avoids spurious re-triggers)
            _dwellTicks = 0;

            // Opacity animation step — advance reveal/dismiss animation if in progress.
            // Reveal: step +0.5 per tick (0 → 0.5 → 1.0 in 2 ticks = 200ms).
            // Dismiss: step -0.5 per tick (1.0 → 0.5 → 0.0 in 2 ticks, then Hide()).
            StepOpacity();
            return;
        }

        // Hover detection: z-order gated — only treat cursor as "over the overlay" when
        // WindowFromPoint confirms our HWND is the topmost at that point.
        var cursorInOverlay = cursorOverOverlay;

        // Post-interaction cooldown: suppress dwell while cursor is still on the overlay
        // after the user clicked an icon (avoids ghost re-show). Clear when cursor exits.
        if (_awaitingOverlayExit)
        {
            _logger.LogDebug(
                "HoverCtrl: formHidden-branch-enter (_awaitingOverlayExit=true) cursor={CX},{CY} cursorInOverlay={CursorIn} _dwellTicks={DT}",
                cursor.X, cursor.Y, cursorInOverlay, _dwellTicks);
            if (!cursorInOverlay)
            {
                _awaitingOverlayExit = false;
                _logger.LogDebug("HoverCtrl: cursor exited overlay; post-interaction cooldown lifted");
            }
            // Skip dwell accumulation regardless (cursor still on overlay or just left)
            _dwellTicks = 0;
            _wasInOverlayLastTick = cursorInOverlay;
            _wasGeometricallyInOverlayLastTick = overlayBounds.Contains(cursor);
            return;
        }

        if (cursorInOverlay)
        {
            if (!_wasInOverlayLastTick)
            {
                _logger.LogDebug("HoverCtrl: cursor entered overlay bounds; starting dwell");
                _logger.LogDebug(
                    "HoverCtrl: formHidden-branch-enter (first cursor-in-overlay tick) cursor={CX},{CY} cursorInOverlay=true _dwellTicks={DT} _awaitingOverlayExit={AwaitExit}",
                    cursor.X, cursor.Y, _dwellTicks, _awaitingOverlayExit);
            }

            var prevDwellTicks = _dwellTicks;
            _dwellTicks++;
            if (prevDwellTicks == 0)
                _logger.LogDebug("HoverCtrl: dwell-began cursor={CX},{CY}", cursor.X, cursor.Y);
            if (_dwellTicks >= DwellThresholdTicks)
            {
                _logger.LogDebug("HoverCtrl: dwell threshold reached at {Cursor}, calling TryShowForm", cursor);
                TryShowForm(cursor);
                _dwellTicks = 0; // reset so a continuous dwell doesn't spam Show
            }
        }
        else
        {
            // F4: symmetric exit log — fires on the first tick the cursor leaves the overlay
            // when the form is not visible (mirrors the "cursor entered overlay bounds" log).
            if (_wasInOverlayLastTick)
                _logger.LogDebug("HoverCtrl: cursor exited overlay bounds (no form visible)");
            _dwellTicks = 0;
        }

        _wasInOverlayLastTick = cursorInOverlay;
        _wasGeometricallyInOverlayLastTick = overlayBounds.Contains(cursor);
    }

    /// <summary>
    /// Called by the overlay after a left-click activates a session or workspace.
    /// Dismisses the dashboard immediately and resets all dwell/grace state so the next
    /// hover starts cleanly. Bypasses the 300ms grace corridor (which would otherwise
    /// create a dead zone right after click where new hovers can't trigger).
    /// </summary>
    public void HandleSurfaceInteraction()
    {
        _logger.LogDebug("HoverCtrl: HandleSurfaceInteraction received from overlay");
        ForceHideForm();
        _awaitingOverlayExit = true;
        _logger.LogDebug("HoverCtrl: post-interaction cooldown set — dwell suppressed until cursor exits overlay");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _logger.LogDebug("HoverCtrl: Dispose called");
        ForceHideForm();
    }

    // --- Private helpers ---

    private void TryShowForm(Point cursor)
    {
        _logger.LogDebug(
            "HoverCtrl: TryShowForm at cursor={CX},{CY} _dwellTicks={DT} _awaitingOverlayExit={AwaitExit}",
            cursor.X, cursor.Y, _dwellTicks, _awaitingOverlayExit);

        if (!_overlayWindow.TryGetSessionIdAtScreenPoint(cursor, out var sessionId))
        {
            var overlayBoundsForLog = _overlayWindow.ActualScreenBounds;
            _logger.LogDebug(
                "HoverCtrl: TryShowForm aborted — no session under cursor at {CX},{CY} actualBounds={BX},{BY},{BW},{BH}",
                cursor.X, cursor.Y,
                overlayBoundsForLog.X, overlayBoundsForLog.Y, overlayBoundsForLog.Width, overlayBoundsForLog.Height);
            return;
        }

        _logger.LogDebug("HoverCtrl: TryShowForm session={SessionId}, recreating form", sessionId);
        _hoveredSessionId = sessionId;

        // Always dispose any prior form before creating a fresh one.
        // A fresh HWND is needed so PinWindowToAllDesktops targets the current form instance.
        DisposeForm();

        var sessions = _sessionSource();
        var entry = sessions.FirstOrDefault(e => e.SessionId == sessionId);
        if (entry is null)
        {
            _logger.LogDebug(
                "HoverCtrl: TryShowForm aborted — no SessionEntry for {SessionId}; available sessions=[{Available}] cursor={CX},{CY}",
                sessionId,
                string.Join(",", sessions.Select(s => s.SessionId)),
                cursor.X, cursor.Y);
            return;
        }

        var cachedGit = _gitCache.TryGetCached(entry.State.Cwd);
        var vm = LiveDashboardVmBuilder.BuildForSession(entry, _hookAccumulationStore, cachedGit, _sessionSource(), DateTimeOffset.UtcNow);

        _form = new DashboardForm(vm, _loggerFactory);

        // Anchor-aware placement: decide above-or-below the overlay, then fix the
        // NEAR edge of the form to the overlay so growth goes AWAY from the overlay.
        // DashboardAnchor.Bottom → form's Bottom edge is fixed, form grows upward.
        // DashboardAnchor.Top    → form's Top edge is fixed, form grows downward.
        var workingArea = Screen.FromControl(_overlayWindow).WorkingArea;
        var overlayBounds = _overlayWindow.ActualScreenBounds;

        var currentHeight = _form.Height;
        var spaceBelow = workingArea.Bottom - (overlayBounds.Bottom + BridgeGap);
        var spaceAbove = (overlayBounds.Top - BridgeGap) - workingArea.Top;

        DashboardAnchor anchorMode;
        int anchorY;
        if (spaceBelow >= currentHeight)
        {
            anchorMode = DashboardAnchor.Top;
            anchorY    = overlayBounds.Bottom + BridgeGap; // form's TOP at this Y; grows down
        }
        else if (spaceAbove >= currentHeight)
        {
            anchorMode = DashboardAnchor.Bottom;
            anchorY    = overlayBounds.Top - BridgeGap;    // form's BOTTOM at this Y; grows up
        }
        else
        {
            // Degenerate: not enough room either way — pick the side with more room.
            if (spaceBelow >= spaceAbove)
            {
                anchorMode = DashboardAnchor.Top;
                anchorY    = overlayBounds.Bottom + BridgeGap;
            }
            else
            {
                anchorMode = DashboardAnchor.Bottom;
                anchorY    = overlayBounds.Top - BridgeGap;
            }
        }

        // X: centre on cursor, then clamp to working area (width is fixed at 520).
        var anchorX = Math.Max(workingArea.Left,
            Math.Min(cursor.X - _form.Width / 2, workingArea.Right - _form.Width));

        _logger.LogDebug(
            "HoverCtrl: form-place-decision overlay={OX},{OY},{OW},{OH} cursor={CX},{CY} workingArea={WAX},{WAY},{WAW},{WAH} formHeight={FH} formWidth={FW} spaceBelow={SB} spaceAbove={SA} → anchorMode={Mode} anchorX={AX} anchorY={AY}",
            overlayBounds.X, overlayBounds.Y, overlayBounds.Width, overlayBounds.Height,
            cursor.X, cursor.Y,
            workingArea.X, workingArea.Y, workingArea.Width, workingArea.Height,
            _form.Height, _form.Width,
            spaceBelow, spaceAbove,
            anchorMode, anchorX, anchorY);

        _form.PlaceWithAnchor(anchorX, anchorY, anchorMode);
        // Opacity starts at 0 — the drain-tick StepOpacity() will step it up.
        _form.Opacity = 0.0;
        _opacityDirection = 1; // reveal in progress
        _logger.LogDebug("HoverCtrl: opacity-reveal-started");
        _form.Show(vm);
        _logger.LogDebug(
            "HoverCtrl: form shown at Location={Location} Bounds={Bounds} hwnd={Handle}",
            _form.Location, _form.Bounds, _form.Handle);
        _ticksSinceLastRefresh = 0; // first throttled refresh fires ~1s after show

        // Post-show anchor verification: log expected vs actual edge so drift is visible.
        var expectedEdgeY = anchorMode == DashboardAnchor.Top ? anchorY : anchorY;
        var actualEdgeY   = anchorMode == DashboardAnchor.Top ? _form.Top : _form.Bottom;
        _logger.LogDebug(
            "HoverCtrl: post-show form-bounds={FB} anchorMode={Mode} anchorY={AY} expectedEdgeY={EE} actualEdgeY={AE} delta={D}",
            _form.Bounds, anchorMode, anchorY, expectedEdgeY, actualEdgeY, actualEdgeY - expectedEdgeY);
        // Pin to all virtual desktops so the form is visible regardless of which desktop
        // the user is on. IVirtualDesktopPinnedApps::PinView is the COM equivalent of
        // Task View right-click → "Show this window on all desktops". Idempotent.
        _desktopManager.PinWindowToAllDesktops(_form.Handle);
        _logger.LogDebug("HoverCtrl: form pinned to all desktops");
        _outsideTicks = 0;
        _overlayWindow.IsDashboardHoverActive = true;

        // If git info was not cached, kick off async fetch on the thread pool.
        // When it returns, marshal back to the UI thread via _overlayWindow (long-lived stable
        // control) and update the form if still visible. A local snapshot of _form is captured
        // on the UI thread before the thread switch; _overlayWindow.BeginInvoke is used instead
        // of _form?.BeginInvoke to avoid a cross-thread race where _form could be disposed
        // between the null-check and the BeginInvoke dispatch.
        if (cachedGit is null && !string.IsNullOrEmpty(entry.State.Cwd))
        {
            var cwd = entry.State.Cwd;
            var formSnapshot = _form;
            Task.Run(() => _gitCache.FetchAndStore(cwd))
                .ContinueWith(_ =>
                {
                    _overlayWindow.BeginInvoke(() =>
                    {
                        if (formSnapshot is null || formSnapshot.IsDisposed)
                            return;

                        var newGit = _gitCache.TryGetCached(cwd);
                        if (newGit is null)
                            return;

                        var currentEntry = _sessionSource().FirstOrDefault(e => e.SessionId == sessionId);
                        if (currentEntry is null)
                            return;

                        // Rebuild with the now-available git info.
                        var updatedVm = LiveDashboardVmBuilder.BuildForSession(currentEntry, _hookAccumulationStore, newGit, _sessionSource(), DateTimeOffset.UtcNow);
                        _logger.LogDebug("HoverCtrl: git async update arrived for {SessionId}, branch={Branch}", sessionId, newGit.Branch);
                        formSnapshot.Update(updatedVm);
                    });
                });
        }
    }

    /// <summary>
    /// Rebuilds the <see cref="DashboardViewModel"/> for <paramref name="sessionId"/> and
    /// applies it to the currently-visible form via <see cref="DashboardForm.Update"/>.
    /// Used for live-session-switch: form stays visible, only data refreshes.
    /// If git info is not cached, kicks off the same async fetch path as <see cref="TryShowForm"/>.
    /// </summary>
    private void RebuildAndApplyUpdate(string sessionId)
    {
        if (_form is null || _form.IsDisposed)
            return;

        var entry = _sessionSource().FirstOrDefault(e => e.SessionId == sessionId);
        if (entry is null)
        {
            _logger.LogDebug("HoverCtrl: RebuildAndApplyUpdate aborted — no SessionEntry for {SessionId}", sessionId);
            return;
        }

        var cachedGit = _gitCache.TryGetCached(entry.State.Cwd);
        var vm = LiveDashboardVmBuilder.BuildForSession(entry, _hookAccumulationStore, cachedGit, _sessionSource(), DateTimeOffset.UtcNow);
        _form.Update(vm);

        // Kick off async git fetch if not cached — same pattern as TryShowForm.
        if (cachedGit is null && !string.IsNullOrEmpty(entry.State.Cwd))
        {
            var cwd = entry.State.Cwd;
            var formSnapshot = _form;
            Task.Run(() => _gitCache.FetchAndStore(cwd))
                .ContinueWith(_ =>
                {
                    _overlayWindow.BeginInvoke(() =>
                    {
                        if (formSnapshot is null || formSnapshot.IsDisposed)
                            return;

                        var newGit = _gitCache.TryGetCached(cwd);
                        if (newGit is null)
                            return;

                        // Only apply update if this session is still the one being shown.
                        if (_hoveredSessionId != sessionId)
                            return;

                        var currentEntry = _sessionSource().FirstOrDefault(e => e.SessionId == sessionId);
                        if (currentEntry is null)
                            return;

                        // Rebuild with the now-available git info.
                        var updatedVm = LiveDashboardVmBuilder.BuildForSession(currentEntry, _hookAccumulationStore, newGit, _sessionSource(), DateTimeOffset.UtcNow);
                        _logger.LogDebug("HoverCtrl: git async update (session-switch) arrived for {SessionId}, branch={Branch}", sessionId, newGit.Branch);
                        formSnapshot.Update(updatedVm);
                    });
                });
        }
    }

    /// <summary>
    /// Starts the dismiss opacity animation (1.0 → 0.5 → 0.0 across 2 drain ticks).
    /// If the form is already animating out or already hidden/disposed, this is a no-op.
    /// After the animation completes, StepOpacity() calls form.Hide() then DisposeOnHide() cleans up.
    /// For the HandleSurfaceInteraction path (immediate forced dismiss), call ForceHideForm() instead.
    /// </summary>
    private void HideForm()
    {
        _logger.LogDebug("HoverCtrl: HideForm called — starting dismiss animation");
        if (_form is null || _form.IsDisposed)
        {
            // Already gone — just reset state
            _hoveredSessionId = null;
            _outsideTicks = 0;
            _dwellTicks = 0;
            _overlayWindow.IsDashboardHoverActive = false;
            return;
        }

        if (_opacityDirection == -1)
        {
            // Already dismissing — don't restart animation
            return;
        }

        // Start dismiss animation
        _opacityDirection = -1;
        _logger.LogDebug("HoverCtrl: opacity-dismiss-started");
        _hoveredSessionId = null;
        _outsideTicks = 0;
        _dwellTicks = 0;
        _overlayWindow.IsDashboardHoverActive = false;

        if (_form.IsPinned)
            _form.Unpin();
    }

    /// <summary>
    /// Immediately disposes the form without animation — used by HandleSurfaceInteraction
    /// and Dispose() where we need instant cleanup.
    /// </summary>
    private void ForceHideForm()
    {
        _logger.LogDebug("HoverCtrl: ForceHideForm called");
        DisposeForm();
        _hoveredSessionId = null;
        _outsideTicks = 0;
        _dwellTicks = 0;
        _overlayWindow.IsDashboardHoverActive = false;
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

    /// <summary>
    /// Advances the Opacity animation by one step (+0.5 for reveal, -0.5 for dismiss).
    /// Reveal completes at 1.0 (stops animation). Dismiss completes at 0.0 (calls Hide()).
    /// Called once per drain tick while the form is visible.
    /// </summary>
    private void StepOpacity()
    {
        if (_form is null || _opacityDirection == 0) return;

        if (_opacityDirection == 1)
        {
            // Reveal
            var next = _form.Opacity + 0.5;
            if (next >= 1.0)
            {
                _form.Opacity = 1.0;
                _opacityDirection = 0;
                _logger.LogDebug("HoverCtrl: reveal complete (Opacity=1.0)");
            }
            else
            {
                _form.Opacity = next;
            }
        }
        else if (_opacityDirection == -1)
        {
            // Dismiss
            var next = _form.Opacity - 0.5;
            if (next <= 0.0)
            {
                _form.Opacity = 0.0;
                _opacityDirection = 0;
                _logger.LogDebug("HoverCtrl: dismiss complete (Opacity=0.0), disposing form");
                // Animation complete — dispose the form (hide then dispose, same as DisposeForm pattern)
                DisposeForm();
            }
            else
            {
                _form.Opacity = next;
            }
        }
    }

    /// <summary>
    /// Computes the bounding rectangle that spans both <paramref name="overlayBounds"/> and
    /// <paramref name="formBounds"/>, expanded by <see cref="BridgeGap"/> on all sides so
    /// the cursor has a 12px corridor between the two windows without triggering dismissal.
    /// </summary>
    private static Rectangle ComputeUnionWithBridge(Rectangle overlayBounds, Rectangle formBounds)
    {
        var union = Rectangle.Union(overlayBounds, formBounds);
        return Rectangle.Inflate(union, BridgeGap, BridgeGap);
    }

}
