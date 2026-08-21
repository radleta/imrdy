using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Imrdy.Core.Desktop;
using Imrdy.Core.Time;
using Imrdy.Windows.Theme;
using Microsoft.Extensions.Logging;

namespace Imrdy.Windows.Dashboard;

/// <summary>
/// Abstract base for hover dashboard forms. Owns the shared shell: layered-window
/// plumbing (FormBorderStyle.None, TopMost, ShowInTaskbar=false), DWM mica/acrylic
/// application, rounded Region clip, focus guard (WM_MOUSEACTIVATE), pin/unpin API,
/// Escape key handler, and anchor-edge placement logic.
///
/// Derived classes (SessionDashboardForm, WorkspaceDashboardForm) provide only the
/// content area: child controls, layout, and view-model update logic specific to
/// their context.
/// </summary>
internal abstract class HoverDashboardFormBase : Form
{
    // WM_MOUSEACTIVATE Win32 constants
    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int MA_ACTIVATE      = 1;
    private const int MA_NOACTIVATE    = 3;

    // 12px bridge gap between overlay bottom and form top — used by subclasses and by
    // ComputeAnchorPlacement. Stored on the base so WorkspaceDashboardForm uses the same gap.
    protected const int BridgeGap = 12;

    private bool _isPinned;
    protected readonly IDesktopManager? _desktopManager;
    protected readonly ILogger _baseLogger;

    // Anchor-aware placement — set by PlaceWithAnchor, reapplied in OnResize/OnSizeChanged.
    private int _anchorX;
    private int _anchorY;
    private DashboardAnchor _anchorMode;

    // The form's minimum client width matches MinimumSize.Width (520).
    // Declared on the base so derived classes can reference it for seeding inner widths.
    protected const int FormMinWidth = 520;

    /// <summary>
    /// Returns true when the form is pinned (the user has clicked the body once).
    /// Pinned forms respond to keyboard nav and are not dismissed by the grace-corridor timeout.
    /// </summary>
    public bool IsPinned => _isPinned;

    /// <summary>
    /// Suppresses implicit activation on <c>Show()</c>. This is not obvious from the other
    /// shell settings, so two reasons, both required:
    /// (1) the dashboard must never steal foreground from the user's terminal — the existing
    ///     <see cref="WM_MOUSEACTIVATE"/> guard in <see cref="WndProc"/> only covers *click*
    ///     activation and does nothing for a programmatic <c>Show()</c> call, which by default
    ///     (<c>ShowWithoutActivation == false</c>) genuinely activates the window via SW_SHOW;
    /// (2) a form that was never activated does not change the OS active window when it is
    ///     closed/disposed. Without this override, tearing down a visible-but-activated
    ///     dashboard (e.g. via the ordinary drain-tick <c>HideForm()</c> → <c>DisposeForm()</c>
    ///     fade-dismiss path firing while a right-click <see cref="ContextMenuStrip"/> happens
    ///     to be open over the overlay) flips the active window, which trips
    ///     <c>ToolStripManager.ModalMenuFilter</c> and force-closes the open menu.
    /// </summary>
    protected override bool ShowWithoutActivation => true;

    /// <summary>
    /// Initializes the shared shell: FormBorderStyle.None, TopMost, ShowInTaskbar=false,
    /// Manual StartPosition, MinimumSize, AutoSize/GrowAndShrink, KeyPreview.
    /// Opacity is left at 0.0 — the live path expects the controller to step it up via
    /// the drain tick; the preview path passes isPinned=true to the derived ctor which
    /// then sets Opacity = 1.0 before calling base.
    /// </summary>
    /// <param name="desktopManager">
    /// May be null — five headless callers (render, inspect, preview) pass null per D9.
    /// When non-null, used by <see cref="PinAcrossVirtualDesktops"/>.
    /// </param>
    /// <param name="loggerFactory">Required; throws <see cref="ArgumentNullException"/> if null.</param>
    protected HoverDashboardFormBase(IDesktopManager? desktopManager, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _desktopManager = desktopManager;
        _baseLogger     = loggerFactory.CreateLogger<HoverDashboardFormBase>();

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar   = false;
        TopMost         = true;
        StartPosition   = FormStartPosition.Manual;
        // Width is driven by MinimumSize; do not set Width directly.
        MinimumSize = new Size(FormMinWidth, 0);
        // MaximumSize must stay at Size.Empty — never set MaximumSize.Height = 0 explicitly
        // because that collapses the form to zero height (documented MS gotcha).
        Debug.Assert(MaximumSize == Size.Empty || MaximumSize.Height > 0,
            "MaximumSize.Height must not be 0 — it collapses form height.");
        AutoSize     = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        KeyPreview   = true; // Required: without this, child controls intercept KeyDown before the form
        Opacity      = 0.0;  // Live path: controller steps it up. Preview path: derived ctor sets 1.0.
    }

    // ---- Public API ----

    /// <summary>
    /// Pins the form without transferring focus.
    /// Per Decision D11, the first body click pins without activating.
    /// Do NOT call this.Activate() here.
    /// </summary>
    public void Pin()
    {
        _isPinned = true;
        OnPinChanged(pinned: true);
        _baseLogger.LogDebug("HoverDashboardFormBase: Pin() called; _isPinned true");
    }

    /// <summary>
    /// Unpins the form. After Unpin(), the next grace-corridor tick will dismiss it.
    /// </summary>
    public void Unpin()
    {
        _isPinned = false;
        OnPinChanged(pinned: false);
        _baseLogger.LogDebug("HoverDashboardFormBase: Unpin() called; _isPinned false");
    }

    /// <summary>
    /// Called by <see cref="Pin"/> and <see cref="Unpin"/> after the <see cref="IsPinned"/>
    /// field is updated. Derived classes override to show/hide pin-sensitive controls
    /// (e.g. keyboard-hints row in SessionDashboardForm).
    /// </summary>
    protected virtual void OnPinChanged(bool pinned) { }

    /// <summary>
    /// Place the form using an edge-anchored model. Call this BEFORE Show(); subsequent
    /// Resize events recompute Location to keep the anchored edge fixed at the screen Y.
    /// </summary>
    /// <param name="anchorX">Screen X for the form's Left edge (form width is fixed).</param>
    /// <param name="anchorY">Screen Y for the anchored edge — Top edge if mode=Top, Bottom edge if mode=Bottom.</param>
    /// <param name="mode">Which edge of the form is anchored.</param>
    public void PlaceWithAnchor(int anchorX, int anchorY, DashboardAnchor mode)
    {
        _anchorX    = anchorX;
        _anchorY    = anchorY;
        _anchorMode = mode;
        ApplyAnchor();
    }

    /// <summary>
    /// Computes the anchor placement (mode + screen coordinates) given the overlay bounds
    /// and cursor position. Centralizes the above/below/fallback decision so both
    /// SessionHoverDashboardController and WorkspaceHoverDashboardController use the same logic.
    /// </summary>
    /// <param name="overlayBounds">Screen bounds of the overlay window.</param>
    /// <param name="cursor">Current cursor screen position.</param>
    /// <param name="workingArea">The monitor's working area (excludes taskbar).</param>
    /// <returns>Tuple of (anchorMode, anchorX, anchorY) to pass to <see cref="PlaceWithAnchor"/>.</returns>
    public (DashboardAnchor anchorMode, int anchorX, int anchorY) ComputeAnchorPlacement(
        Rectangle overlayBounds,
        Point cursor,
        Rectangle workingArea)
    {
        var currentHeight = this.Height;
        var spaceBelow    = workingArea.Bottom - (overlayBounds.Bottom + BridgeGap);
        var spaceAbove    = (overlayBounds.Top  - BridgeGap) - workingArea.Top;

        DashboardAnchor anchorMode;
        int anchorY;
        if (spaceBelow >= currentHeight)
        {
            anchorMode = DashboardAnchor.Top;
            anchorY    = overlayBounds.Bottom + BridgeGap;
        }
        else if (spaceAbove >= currentHeight)
        {
            anchorMode = DashboardAnchor.Bottom;
            anchorY    = overlayBounds.Top - BridgeGap;
        }
        else
        {
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

        // X: keep the form directly above the docked overlay panel, biased toward the
        // hovered chip. The form width is fixed (FormMinWidth), so centring it on the
        // cursor would clamp a left/right-docked overlay's popup hard against the screen
        // edge — reading as "stuck to the screen end" rather than "above the overlay".
        // Instead, constrain the form's span to the overlay's span (sliding toward the
        // cursor within that range); fall back to centring over the overlay when the form
        // is wider than the overlay. A final working-area clamp covers multi-monitor edges.
        int anchorX;
        var lowBound  = overlayBounds.Left;
        var highBound = overlayBounds.Right - this.Width;
        if (lowBound <= highBound)
            anchorX = Math.Max(lowBound, Math.Min(cursor.X - this.Width / 2, highBound));
        else
            anchorX = overlayBounds.Left + (overlayBounds.Width - this.Width) / 2;
        anchorX = Math.Max(workingArea.Left, Math.Min(anchorX, workingArea.Right - this.Width));

        return (anchorMode, anchorX, anchorY);
    }

    /// <summary>
    /// Pins this window to all virtual desktops via IVirtualDesktopPinnedApps so it appears
    /// regardless of which desktop the user is on. Idempotent. No-op when desktopManager is null
    /// (headless callers per D9 pass null). COM-exception absorbing lives inside IDesktopManager.
    /// </summary>
    internal void PinAcrossVirtualDesktops()
    {
        _desktopManager?.PinWindowToAllDesktops(this.Handle);
    }

    // ---- WndProc ----

    /// <summary>
    /// WM_MOUSEACTIVATE policy: MA_NOACTIVATE when unpinned (click delivered but no focus theft);
    /// MA_ACTIVATE when pinned (second click transfers focus for keyboard nav).
    /// Per Raymond Chen: for WM_MOUSEACTIVATE, set m.Result and return — no base call.
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_MOUSEACTIVATE)
        {
            m.Result = (IntPtr)(_isPinned ? MA_ACTIVATE : MA_NOACTIVATE);
            _baseLogger.LogDebug("HoverDashboardFormBase: WM_MOUSEACTIVATE → {Result}",
                _isPinned ? "MA_ACTIVATE (pinned)" : "MA_NOACTIVATE (unpinned)");
            return; // NO base.WndProc — per Raymond Chen, base clobbers MA_NOACTIVATE intent
        }
        base.WndProc(ref m);
    }

    /// <summary>
    /// Escape key while pinned: unpins and hides the form.
    /// Derived classes that need additional Escape behaviour (e.g. <see cref="SessionDashboardForm"/>
    /// exit on preview mode) should check <see cref="IsPinned"/> and handle their branch BEFORE
    /// calling <c>base.OnKeyDown(e)</c>, which will call <see cref="Unpin"/> when Escape is pressed.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape && IsPinned)
        {
            e.Handled = true;
            _baseLogger.LogDebug("HoverDashboardFormBase: Escape while pinned; unpinning and hiding");
            Unpin();
            Hide();
        }
        base.OnKeyDown(e);
    }

    /// <summary>
    /// First body click: pins the form without activating it.
    /// WM_MOUSEACTIVATE has already returned MA_NOACTIVATE at this point.
    /// </summary>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        _baseLogger.LogDebug(
            "HoverDashboardFormBase: OnMouseDown button={Button} at {Location}; _isPinned={WasPinned}",
            e.Button, e.Location, _isPinned);
        base.OnMouseDown(e);
        if (!_isPinned)
            Pin();
    }

    /// <summary>
    /// Rounded Region clip for the form shape. DWM mica is intentionally NOT applied:
    /// the form fades via Form.Opacity (a layered window) and paints an opaque BgForm
    /// fill, so the system backdrop is never visible — its only effect was compositing
    /// opaque white into the Region-carved corners (worst as a flicker during resize
    /// re-clips). Omitting mica keeps the carved corners transparent (desktop shows
    /// through) with no white artifact. Contrast with OverlayPanel, which is never
    /// layered (no fade) and so safely uses DWM corner rounding + mica.
    /// Derived classes that override OnHandleCreated MUST call base.OnHandleCreated(e) FIRST
    /// so the Region is applied before any derived initialization.
    /// </summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyRoundedRegion();
        _baseLogger.LogDebug(
            "HoverDashboardFormBase: handle-created formBounds={Bounds} clientSize={CW}x{CH} regionRadius=14",
            this.Bounds, this.ClientSize.Width, this.ClientSize.Height);
    }

    /// <summary>
    /// Re-apply the rounded Region clip when the form auto-sizes due to row toggling.
    /// Also reapplies the Bottom anchor so the form's bottom edge stays fixed when
    /// TableLayoutPanel relayout grows the height (e.g., optional rows toggled in Update()).
    /// </summary>
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ApplyRoundedRegion();
        var reanchored = _anchorMode == DashboardAnchor.Bottom;
        if (reanchored) ApplyAnchor();
        _baseLogger.LogDebug(
            "HoverDashboardFormBase: resize fired Bounds={Bounds} anchorMode={Mode} anchorY={AY} reanchored={Reanchored}",
            this.Bounds, _anchorMode, _anchorY, reanchored);
    }

    /// <summary>
    /// Belt-and-suspenders: TableLayoutPanel AutoSize sometimes fires SizeChanged without
    /// a corresponding OnResize. Keep the Bottom anchor honest on both paths.
    /// </summary>
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        var reanchored = _anchorMode == DashboardAnchor.Bottom;
        if (reanchored) ApplyAnchor();
    }

    // ---- Protected helpers (callable from derived) ----

    /// <summary>
    /// Reapplies the anchor-edge constraint to Location. Called from PlaceWithAnchor
    /// and from OnResize/OnSizeChanged when the form's height may have changed.
    /// No-op when anchorY is 0 and anchorX is 0 (unset state — form hasn't been placed yet).
    /// </summary>
    protected void ApplyAnchor()
    {
        int newLeft = _anchorX;
        int newTop  = _anchorMode == DashboardAnchor.Top
            ? _anchorY
            : _anchorY - this.Height;
        if (this.Left != newLeft || this.Top != newTop)
        {
            this.Location = new Point(newLeft, newTop);
            _baseLogger.LogDebug(
                "HoverDashboardFormBase: anchor applied mode={Mode} anchorX={AX} anchorY={AY} formHeight={H} → Location={L},{T} bounds={Bounds}",
                _anchorMode, _anchorX, _anchorY, this.Height, newLeft, newTop, this.Bounds);
        }
    }

    /// <summary>
    /// Formats a duration as a compact human-readable string without trailing unit:
    /// "18s" | "2m" | "1h 14m" | "3d". Callers add contextual prefixes/suffixes
    /// (e.g. "for 18s", "idle 2m ago", "42m old").
    /// Promoted to protected static so both SessionDashboardForm and WorkspaceDashboardForm
    /// can call it without each defining their own copy.
    /// Algorithm lives in <see cref="RelativeTimeFormatter.FormatDuration"/>; this wrapper
    /// preserves the call sites in derived forms.
    /// </summary>
    protected static string FormatDuration(TimeSpan span)
        => RelativeTimeFormatter.FormatDuration(span);

    // ---- Protected kbd helpers (shared by SessionDashboardForm and WorkspaceDashboardForm) ----

    /// <summary>
    /// Builds the kbd-styled keyboard hints row for the footer.
    /// Each key glyph is a small Label with a subtle border painted via OnPaint.
    /// Plain separator text sits between key labels with no border.
    /// </summary>
    protected static FlowLayoutPanel MakeKbdHintsFlow()
    {
        var flow = new FlowLayoutPanel
        {
            AutoSize      = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents  = false,
            BackColor     = Color.Transparent,
            Padding       = Padding.Empty,
            Margin        = Padding.Empty,
        };

        // Key glyph font: prefer Cascadia Code; fall back to Consolas.
        Font kbdFont;
        try
        {
            kbdFont = new Font("Cascadia Code", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"HoverDashboardFormBase: Cascadia Code unavailable; falling back to Consolas. {ex.Message}");
            kbdFont = new Font("Consolas", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
        }

        flow.Controls.Add(MakeKbdKey("↑↓", kbdFont));
        flow.Controls.Add(MakeKbdSeparator(" nav  "));
        flow.Controls.Add(MakeKbdKey("↵", kbdFont));
        flow.Controls.Add(MakeKbdSeparator(" switch  "));
        flow.Controls.Add(MakeKbdKey("Esc", kbdFont));
        flow.Controls.Add(MakeKbdSeparator(" close"));

        return flow;
    }

    /// <summary>
    /// Creates a kbd-styled key glyph label: subtle background tint + 1 px painted border.
    /// </summary>
    protected static Label MakeKbdKey(string text, Font font)
    {
        var lbl = new Label
        {
            Text      = text,
            Font      = font,
            ForeColor = ImrdyPalette.FgSecondary,
            BackColor = Color.FromArgb(20, 255, 255, 255),
            AutoSize  = true,
            Padding   = new Padding(5, 1, 5, 1),
            Margin    = new Padding(0, 0, 2, 0),
        };
        lbl.Paint += (_, pe) =>
        {
            var r = new Rectangle(0, 0, lbl.Width - 1, lbl.Height - 1);
            using var pen = new Pen(Color.FromArgb(55, 255, 255, 255), 1f);
            pe.Graphics.DrawRectangle(pen, r);
        };
        return lbl;
    }

    /// <summary>
    /// Creates a plain separator label between kbd key glyphs: no border, no background.
    /// </summary>
    protected static Label MakeKbdSeparator(string text)
        => new()
        {
            Text      = text,
            Font      = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = ImrdyPalette.FgMuted,
            BackColor = Color.Transparent,
            AutoSize  = true,
            Padding   = Padding.Empty,
            Margin    = new Padding(0, 2, 0, 0),
        };

    // ---- Private helpers ----

    /// <summary>
    /// Applies a 14 px radius rounded-rectangle Region to the form.
    /// Called from OnHandleCreated and OnResize so the clip tracks form size changes.
    /// The previous Region is disposed before replacing.
    /// </summary>
    private void ApplyRoundedRegion()
        => ImrdyPalette.ApplyRoundedRegion(this, 14);
}
