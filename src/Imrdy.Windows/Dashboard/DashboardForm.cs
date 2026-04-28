using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Imrdy.Core.Display;
using Imrdy.Core.Status;
using Microsoft.Extensions.Logging;

namespace Imrdy.Windows.Dashboard;

/// <summary>
/// Controls which edge of the form is fixed when the form is resized.
/// Used by <see cref="DashboardForm.PlaceWithAnchor"/> to keep the form abutting a
/// reference surface (the overlay row) even when the form's AutoSize height changes
/// after Show() or after a live-session-switch update changes optional row visibility.
/// </summary>
internal enum DashboardAnchor
{
    /// <summary>Form's TOP edge is fixed at AnchorY; growth is downward.</summary>
    Top,
    /// <summary>Form's BOTTOM edge is fixed at AnchorY; growth is upward (Top = AnchorY - Height).</summary>
    Bottom,
}

/// <summary>
/// Non-layered WinForms hover dashboard. Hosts child controls for every slot
/// from the Dashboard Slot Contracts table. Fade animation (Opacity 0→1 on show,
/// 1→0 on hide) is driven by HoverDashboardController.OnDrainTick in +0.5/-0.5
/// increments — exactly 2 ticks (200ms) per direction.
///
/// DWM mica/acrylic backdrop is applied via DwmSetWindowAttribute on Win11 22H2+;
/// opaque fallback on Win10 19045 (no HRESULT noise in logs).
///
/// Two-click pin/activate (D11): first body click calls Pin() without this.Activate();
/// second click hits WM_MOUSEACTIVATE → MA_ACTIVATE and focus transfers for keyboard nav.
/// </summary>
internal sealed class DashboardForm : Form
{
    // WM_MOUSEACTIVATE Win32 constants
    private const int WM_MOUSEACTIVATE = 0x0021;
    private const int MA_ACTIVATE = 1;
    private const int MA_NOACTIVATE = 3;

    // DWM backdrop constants
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_MAINWINDOW = 2; // Mica

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private bool _isPinned;
    private readonly bool _isPreviewMode;
    private readonly ILogger _logger;

    // Anchor-aware placement — set by PlaceWithAnchor, reapplied in OnResize/OnSizeChanged.
    private int _anchorX;
    private int _anchorY;
    private DashboardAnchor _anchorMode;

    // ---- Layout child controls ----

    // Fleet strip (top)
    private readonly Panel _fleetStrip;
    private readonly Label _fleetLabel;
    private readonly Panel _fleetDotsPanel;
    private readonly Label _fleetCount;

    // Header: accent bar + session name, persona chip, project, cwd, desktop chip
    private readonly Panel _accentBar;
    private readonly Label _sessionNameLabel;
    private readonly Label _personaChip;
    private readonly Label _projectLabel;
    private readonly Label _cwdLabel;
    private readonly Label _desktopChip;

    // Status row
    private readonly Panel _statusPillDot;
    private readonly Label _statusPill;
    private readonly Label _elapsedLabel;
    private readonly Label _turnCountLabel;

    // Session age (inline with elapsed row)
    private readonly Label _sessionAgeLabel;

    // Permission bar (conditional — visible only when status=="permission")
    private readonly Panel _permissionBar;
    private readonly Label _permissionLabel;

    // Current tool section (visible when status=="busy" and CurrentTool != null)
    private readonly Panel _currentToolSection;
    private readonly Label _currentToolSectionLabel; // "RUNNING NOW"
    private readonly Label _currentToolLabel;

    // Last prompt section
    private readonly Panel _lastPromptSection;
    private readonly Label _lastPromptSectionLabel; // "LAST PROMPT"
    private readonly Label _lastPromptLabel;

    // Sparkline
    private readonly Panel _sparklineSection;
    private readonly Label _sparklineSectionLabel; // "ACTIVITY · LAST 60s"
    private readonly SparklineControl _sparkline;

    // Recent tools chip strip
    private readonly Panel _chipsSection;
    private readonly Label _chipsSectionLabel; // "RECENT TOOLS"
    private readonly FlowLayoutPanel _chipsPanel;

    // Footer
    private readonly Panel _footer;
    private readonly Label _gitLabel;
    private readonly Label _subagentsLabel;
    private readonly Label _failureLabel;
    private readonly Control _keyboardHintsLabel; // FlowLayoutPanel containing kbd-styled hint labels

    // TableLayoutPanel row indices — used to toggle row heights
    private TableLayoutPanel _tableLayout = null!; // assigned in BuildLayout, called from ctor
    private const int RowFleet       = 0;
    private const int RowHeader      = 1;
    private const int RowStatus      = 2;
    private const int RowPermission  = 3;
    private const int RowCurrentTool = 4;
    private const int RowLastPrompt  = 5;
    private const int RowSparkline   = 6;
    private const int RowChips       = 7;
    private const int RowFooter      = 8;

    // Fixed pixel heights for each optional row when visible.
    // These are the canonical row heights — do NOT derive from control.Height after Dock=Fill is set.
    // iter-7: bumped to budget section label space (+14 px per optional row).
    private const int HeightPermission  = 62;
    private const int HeightCurrentTool = 72;
    private const int HeightLastPrompt  = 72;
    private const int HeightChips       = 62;

    // The form's minimum client width matches MinimumSize.Width (520).
    // Used to size inner FlowLayoutPanels at construction time before the parent is laid out.
    // These panels update via Anchor=Left|Right on subsequent resizes.
    private const int FormMinWidth = 520;

    // Colors matching dashboard-ultra.html design
    private static readonly Color BgForm       = Color.FromArgb(28, 30, 38);
    private static readonly Color FgPrimary    = Color.FromArgb(232, 234, 240);
    private static readonly Color FgSecondary  = Color.FromArgb(155, 161, 173);
    private static readonly Color FgMuted      = Color.FromArgb(107, 111, 122);
    private static readonly Color BgFleet      = Color.FromArgb(20, 20, 27);
    private static readonly Color BgFooter     = Color.FromArgb(18, 18, 24);
    private static readonly Color Border       = Color.FromArgb(20, 255, 255, 255);

    /// <summary>
    /// Returns true when the form is pinned (the user has clicked the body once).
    /// Pinned forms respond to keyboard nav and are not dismissed by the grace-corridor timeout.
    /// </summary>
    public bool IsPinned => _isPinned;

    public DashboardForm(DashboardViewModel vm, ILoggerFactory loggerFactory, bool isPinned = false, bool isPreviewMode = false)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _logger = loggerFactory.CreateLogger<DashboardForm>();
        _isPinned = isPinned;
        _isPreviewMode = isPreviewMode;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = BgForm;
        // Width is driven by MinimumSize; do not set Width directly.
        MinimumSize = new Size(FormMinWidth, 0);
        // MaximumSize must stay at Size.Empty — never set MaximumSize.Height = 0 explicitly
        // because that collapses the form to zero height (documented MS gotcha).
        Debug.Assert(MaximumSize == Size.Empty || MaximumSize.Height > 0,
            "MaximumSize.Height must not be 0 — it collapses form height.");
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        KeyPreview = true; // Required: without this, child controls intercept KeyDown before the form sees it

        // Preview harness path (isPinned:true) starts fully opaque — no animation.
        // Live path starts at 0 so HoverDashboardController can step it up.
        Opacity = isPinned ? 1.0 : 0.0;

        // Build child controls
        _fleetLabel        = MakeLabel("Fleet", 9, FgMuted, bold: false);
        _fleetDotsPanel    = new Panel { Height = 10, BackColor = Color.Transparent };
        _fleetCount        = MakeLabel("", 10, FgMuted, bold: false);

        _fleetStrip = new Panel
        {
            Dock      = DockStyle.Fill,
            BackColor = BgFleet,
            // Margin=0 lets BgFleet extend to the form edges so the rounded Region
            // (radius 14) clips through panel pixels uniformly. A non-zero margin
            // creates a visible BgForm seam at the top corners.
            Margin    = new Padding(0),
        };
        _fleetStrip.Resize += (_, _) => RepositionFleetStripChildren();

        _accentBar = new Panel { Width = 3, Dock = DockStyle.Left, BackColor = StatusColor("idle") };

        _sessionNameLabel = MakeLabel("", 15, FgPrimary, bold: true);
        // _personaChip: hidden and zero-width; awaits persona feature (deferred per decisions.md D5)
        _personaChip = MakeLabel("", 9, Color.FromArgb(196, 167, 224), bold: false);
        _personaChip.Visible = false;
        _projectLabel     = MakeLabel("", 11, FgPrimary, bold: true);
        _cwdLabel         = MakeLabel("", 10, FgMuted, bold: false);
        _desktopChip      = MakeLabel("", 9, FgSecondary, bold: false);

        _statusPillDot = new Panel { Width = 6, Height = 6, BackColor = StatusColor("idle") };
        _statusPillDot.Paint += (_, pe) =>
        {
            pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var b = new SolidBrush(_statusPillDot.BackColor);
            pe.Graphics.FillEllipse(b, 0, 0, _statusPillDot.Width - 1, _statusPillDot.Height - 1);
        };

        _statusPill      = MakeLabel("", 10, FgPrimary, bold: true);
        _elapsedLabel    = MakeLabel("", 11, FgSecondary, bold: false);
        _sessionAgeLabel = MakeLabel("", 10, FgMuted, bold: false);
        _turnCountLabel  = MakeLabel("", 10, FgMuted, bold: false);

        _permissionBar   = new Panel { Height = 40, BackColor = Color.FromArgb(30, 167, 127, 200), Margin = new Padding(14, 0, 14, 8) };
        _permissionLabel = MakeLabel("", 11, Color.FromArgb(196, 167, 224), bold: false);
        _permissionLabel.Dock = DockStyle.Fill;
        _permissionBar.Controls.Add(_permissionLabel);

        // CurrentTool section: section label + code-style content block.
        // Visible starts true — row height 0 collapses it until SetRowVisible activates it.
        _currentToolSectionLabel = MakeSectionLabel("RUNNING NOW");
        _currentToolLabel = new Label
        {
            Text         = "",
            Font         = new Font("Cascadia Code", 10f, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor    = FgPrimary,
            BackColor    = Color.FromArgb(25, 227, 139, 75),
            AutoSize     = false,
            AutoEllipsis = true,
            Anchor       = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
            Height       = 22,
            Padding      = new Padding(4, 2, 4, 2),
            Margin       = Padding.Empty,
        };
        _currentToolSection = new Panel { Height = 50 };

        // LastPrompt section: section label + prompt text.
        _lastPromptSectionLabel = MakeSectionLabel("LAST PROMPT");
        _lastPromptLabel = new Label
        {
            Text         = "",
            Font         = new Font("Segoe UI", 11f, FontStyle.Italic, GraphicsUnit.Point),
            ForeColor    = FgSecondary,
            BackColor    = Color.Transparent,
            AutoSize     = false,
            AutoEllipsis = true,
            Anchor       = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
            Height       = 22,
            Padding      = Padding.Empty,
            Margin       = Padding.Empty,
        };
        _lastPromptSection = new Panel { Height = 50 };

        // Sparkline section: section label + sparkline control.
        _sparklineSectionLabel = MakeSectionLabel("ACTIVITY · LAST 60s");
        _sparkline = new SparklineControl
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom,
        };
        _sparklineSection = new Panel { Height = 42 };

        // Chips section: section label + chip flow panel.
        _chipsSectionLabel = MakeSectionLabel("RECENT TOOLS");
        _chipsPanel = new FlowLayoutPanel
        {
            Anchor        = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents  = false,
            Padding       = Padding.Empty,
            AutoScroll    = false,
            Height        = 24,
        };
        _chipsSection = new Panel { Height = 38 };

        _gitLabel           = MakeLabel("", 10, FgSecondary, bold: false);
        _gitLabel.Visible   = false;
        _subagentsLabel     = MakeLabel("", 10, Color.FromArgb(196, 167, 224), bold: false);
        _subagentsLabel.Visible = false;
        _failureLabel       = MakeLabel("", 10, Color.FromArgb(212, 108, 90), bold: false);
        _failureLabel.Visible = false;
        _keyboardHintsLabel = MakeKbdHintsFlow();
        _keyboardHintsLabel.Visible = false;

        _footer = new Panel
        {
            Dock      = DockStyle.Fill,
            BackColor = BgFooter,
            // See _fleetStrip Margin comment — same reason.
            Margin    = new Padding(0),
        };

        BuildLayout();
        Update(vm);
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
        _keyboardHintsLabel.Visible = true;
        _logger.LogDebug("DashboardForm: Pin() called; _isPinned true");
    }

    /// <summary>
    /// Unpins the form. After Unpin(), the next grace-corridor tick will dismiss it.
    /// </summary>
    public void Unpin()
    {
        _isPinned = false;
        _keyboardHintsLabel.Visible = false;
        _logger.LogDebug("DashboardForm: Unpin() called; _isPinned false");
    }

    /// <summary>
    /// Place the form using an edge-anchored model. Call this BEFORE Show(); subsequent
    /// Resize events recompute Location to keep the anchored edge fixed at the screen Y.
    /// </summary>
    /// <param name="anchorX">Screen X for the form's Left edge (form width is fixed).</param>
    /// <param name="anchorY">Screen Y for the anchored edge — Top edge if mode=Top, Bottom edge if mode=Bottom.</param>
    /// <param name="mode">Which edge of the form is anchored.</param>
    public void PlaceWithAnchor(int anchorX, int anchorY, DashboardAnchor mode)
    {
        _anchorX = anchorX;
        _anchorY = anchorY;
        _anchorMode = mode;
        ApplyAnchor();
    }

    /// <summary>
    /// Shows (or re-shows) the form anchored to the given screen location.
    /// Kept for legacy call sites; new code should call PlaceWithAnchor + Show(vm).
    /// </summary>
    public void Show(DashboardViewModel vm, Point location)
    {
        Update(vm);
        Location = location;
        if (!Visible)
            base.Show();
    }

    /// <summary>
    /// Shows (or re-shows) the form using the anchor already set by PlaceWithAnchor.
    /// The caller must call PlaceWithAnchor first.
    /// </summary>
    public void Show(DashboardViewModel vm)
    {
        Update(vm);
        if (!Visible)
            base.Show();
        _logger.LogDebug(
            "DashboardForm: post-show-fleet fleetStripBoundsInForm={StripBounds} fleetLabelBoundsInForm={LabelBounds} fleetCountBoundsInForm={CountBounds} fleetDotsBoundsInForm={DotsBounds}",
            _fleetStrip.Bounds,
            new System.Drawing.Rectangle(_fleetStrip.Left + _fleetLabel.Left, _fleetStrip.Top + _fleetLabel.Top, _fleetLabel.Width, _fleetLabel.Height),
            new System.Drawing.Rectangle(_fleetStrip.Left + _fleetCount.Left, _fleetStrip.Top + _fleetCount.Top, _fleetCount.Width, _fleetCount.Height),
            new System.Drawing.Rectangle(_fleetStrip.Left + _fleetDotsPanel.Left, _fleetStrip.Top + _fleetDotsPanel.Top, _fleetDotsPanel.Width, _fleetDotsPanel.Height));
    }

    /// <summary>
    /// Reapplies the anchor-edge constraint to Location. Called from PlaceWithAnchor
    /// and from OnResize/OnSizeChanged when the form's height may have changed.
    /// No-op when anchorY is 0 and anchorX is 0 (unset state — form hasn't been placed yet).
    /// </summary>
    private void ApplyAnchor()
    {
        int newLeft = _anchorX;
        int newTop = _anchorMode == DashboardAnchor.Top
            ? _anchorY
            : _anchorY - this.Height;
        if (this.Left != newLeft || this.Top != newTop)
        {
            this.Location = new Point(newLeft, newTop);
            _logger.LogDebug(
                "DashboardForm: anchor applied mode={Mode} anchorX={AX} anchorY={AY} formHeight={H} → Location={L},{T} bounds={Bounds}",
                _anchorMode, _anchorX, _anchorY, this.Height, newLeft, newTop, this.Bounds);
        }
    }

    /// <summary>
    /// Live-updates all child controls from the view model without a full repaint.
    /// </summary>
    public void Update(DashboardViewModel vm)
    {
        var now = DateTimeOffset.UtcNow;

        // --- Fleet strip ---
        UpdateFleetStrip(vm, now);

        // --- Accent bar color = status color ---
        UpdateAccentBarColor(vm.Status);

        // --- Header ---
        _sessionNameLabel.Text = vm.SessionName;
        // _personaChip excluded from layout (decisions.md D5 — deferred until persona feature lands)
        _projectLabel.Text     = vm.Project;
        _cwdLabel.Text         = FrontTruncatePath(vm.CwdPath, 38);
        _desktopChip.Text      = $"Desktop {vm.DesktopIndex + 1}";

        // --- Status pill ---
        UpdateStatusPill(vm.Status);

        // --- Status row metadata: conditional fragments per status (decisions.md D6) ---
        // busy:        "BUSY · for {elapsed} · {N} turns · {age} old"
        // idle/done:   "{STATUS} · idle {ago} · {N} turns · {age} old"
        // other:       "{STATUS} · {N} turns · {age} old" (elapsed fragment omitted)
        if (vm.Status == "busy")
        {
            var elapsed = now - vm.LastHookAt;
            _elapsedLabel.Text = $"for {FormatDuration(elapsed)}";
        }
        else if (vm.Status is "idle" or "done")
        {
            var ago = now - vm.LastHookAt;
            _elapsedLabel.Text = $"idle {FormatDuration(ago)} ago";
        }
        else
        {
            _elapsedLabel.Text = "";
        }

        // --- Session age (now - StartedAt) ---
        _sessionAgeLabel.Text = $"{FormatDuration(now - vm.StartedAt)} old";

        // --- Turn count ---
        _turnCountLabel.Text = vm.TurnCount > 0 ? $"{vm.TurnCount} turns" : "";

        // --- Permission bar (visible only when status==permission) ---
        var showPermission = vm.Status == "permission" && vm.PermissionTool is not null;
        if (showPermission)
            _permissionLabel.Text = "Awaiting permission: " + vm.PermissionTool;
        SetRowVisible(RowPermission, showPermission, HeightPermission);

        // --- Current tool (visible ONLY when status==busy and CurrentTool is set) ---
        var showCurrentTool = vm.Status == "busy" && vm.CurrentTool is not null;
        if (showCurrentTool)
            _currentToolLabel.Text = vm.CurrentTool!;
        SetRowVisible(RowCurrentTool, showCurrentTool, HeightCurrentTool);

        // --- Last prompt ---
        var showLastPrompt = vm.LastPrompt is not null;
        if (showLastPrompt)
            _lastPromptLabel.Text = "“" + vm.LastPrompt + "”";
        SetRowVisible(RowLastPrompt, showLastPrompt, HeightLastPrompt);

        // --- Sparkline ---
        _sparkline.ReferenceTime = vm.LastHookAt;
        _sparkline.Timestamps = vm.ActivityTimestamps;

        // --- Tool chips ---
        UpdateChips(vm.RecentTools);

        // --- Footer chips ---
        // Git chip: hidden when null
        _gitLabel.Visible = vm.Git is not null;
        if (vm.Git is not null)
        {
            _gitLabel.Text = vm.Git.DirtyCount > 0
                ? $"⎇ {vm.Git.Branch} +{vm.Git.DirtyCount}"
                : $"⎇ {vm.Git.Branch}";
        }

        // Subagent count chip: hidden when 0; ⁂ (U+2042 ASTERISM) per mockup
        _subagentsLabel.Visible = vm.SubagentCount > 0;
        if (vm.SubagentCount > 0)
            _subagentsLabel.Text = $"⁂ {vm.SubagentCount} subagents";

        // Failure count chip: hidden when 0; format "⚠ N failure / M turns"
        _failureLabel.Visible = vm.FailureCount > 0;
        if (vm.FailureCount > 0)
            _failureLabel.Text = $"⚠ {vm.FailureCount} failure / {vm.TurnCount} turns";

        // Keyboard hints: visible only when pinned
        _keyboardHintsLabel.Visible = _isPinned;

        // Phase 2 slots: render nothing when null (no placeholder text).
        // ContextTokens, ContextWindowSize, CostUsd, ModelDisplayName, RateLimits
        // are all null in Phase 1 — the form has no controls for them yet.
    }

    /// <summary>
    /// Updates the vertical accent bar color to match the current status.
    /// Called from Update() on every view model refresh.
    /// </summary>
    public void UpdateAccentBarColor(string status)
    {
        _accentBar.BackColor = StatusColor(status);
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
            _logger.LogDebug("DashboardForm: WM_MOUSEACTIVATE → {Result}",
                _isPinned ? "MA_ACTIVATE (pinned)" : "MA_NOACTIVATE (unpinned)");
            return;
        }
        base.WndProc(ref m);
    }

    /// <summary>
    /// First body click: pins the form without activating it.
    /// WM_MOUSEACTIVATE has already returned MA_NOACTIVATE at this point.
    /// </summary>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        _logger.LogDebug("DashboardForm: OnMouseDown button={Button} at {Location}; _isPinned={WasPinned}",
            e.Button, e.Location, _isPinned);
        base.OnMouseDown(e);
        if (!_isPinned)
            Pin();
    }

    /// <summary>
    /// In preview mode, activate the form on first show so Esc works without a prior click.
    /// FormBorderStyle.None + ShowInTaskbar=false means the form does not steal focus on
    /// Application.Run — Activate() here transfers keyboard focus to the form.
    /// </summary>
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_isPreviewMode)
        {
            _logger.LogDebug("DashboardForm: OnShown in preview mode; calling Activate() for keyboard focus");
            Activate();
        }
    }

    /// <summary>
    /// Esc behavior differs by mode:
    ///   Preview path (_isPreviewMode): calls Application.Exit() to terminate the process.
    ///   Hover path (!_isPreviewMode): Unpin() + Hide() returns the form to idle hover state.
    /// In both cases, the form must be pinned for Escape to fire (keyboard focus requires pin).
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        _logger.LogDebug("DashboardForm: OnKeyDown key={Key} isPinned={IsPinned} isPreviewMode={IsPreviewMode}",
            e.KeyCode, _isPinned, _isPreviewMode);
        if (e.KeyCode == Keys.Escape && _isPinned)
        {
            e.Handled = true;
            if (_isPreviewMode)
            {
                _logger.LogDebug("DashboardForm: Escape in preview mode; calling Application.Exit()");
                Application.Exit();
            }
            else
            {
                _logger.LogDebug("DashboardForm: Escape while pinned; unpinning and hiding");
                Unpin();
                Hide();
            }
        }
        base.OnKeyDown(e);
    }

    /// <summary>
    /// DWM mica/acrylic backdrop + rounded Region clip.
    /// Silently swallows HRESULT failures on Win10 19045.
    /// </summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            var backdropType = DWMSBT_MAINWINDOW;
            var hr = DwmSetWindowAttribute(Handle, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));
            if (hr != 0)
                _logger.LogDebug("DashboardForm: DwmSetWindowAttribute HRESULT=0x{Hr:X8} (expected on Win10)", hr);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "DashboardForm: DwmSetWindowAttribute threw (expected on Win10)");
        }

        ApplyRoundedRegion();
        _logger.LogDebug(
            "DashboardForm: handle-created formBounds={Bounds} clientSize={CW}x{CH} regionRadius=14",
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
        _logger.LogDebug(
            "DashboardForm: resize fired Bounds={Bounds} anchorMode={Mode} anchorY={AY} reanchored={Reanchored}",
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

    // ---- Private helpers ----

    /// <summary>
    /// Applies a 14 px radius rounded-rectangle Region to the form.
    /// Called from OnHandleCreated and OnResize so the clip tracks form size changes.
    /// The previous Region is disposed before replacing.
    /// </summary>
    private void ApplyRoundedRegion()
    {
        if (Width <= 0 || Height <= 0) return;
        const int radius = 14;
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(0, 0, diameter, diameter, 180, 90);
        path.AddArc(Width - diameter, 0, diameter, diameter, 270, 90);
        path.AddArc(Width - diameter, Height - diameter, diameter, diameter, 0, 90);
        path.AddArc(0, Height - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        var oldRegion = Region;
        Region = new Region(path);
        oldRegion?.Dispose();
        path.Dispose();
    }

    private void BuildLayout()
    {
        SuspendLayout();

        // Prepare fleet strip and footer content before adding to table
        LayoutFleetStrip();
        LayoutFooter();

        // Prepare optional section decorations
        _permissionBar.Dock   = DockStyle.Fill;
        _permissionBar.Margin = new Padding(8, 4, 8, 4);

        // Section panels all use Dock=Fill to fill their TLP row.
        // Child controls use Location+Anchor (NOT Dock) so section labels
        // sit at Top and content sits below without Dock stacking conflicts.
        // Label widths are seeded from FormMinWidth so they're correct on first render;
        // Anchor=Left|Right|Top maintains them on subsequent resize.

        // CurrentTool section
        _currentToolSection.Dock      = DockStyle.Fill;
        _currentToolSection.BackColor = Color.FromArgb(15, 227, 139, 75);
        _currentToolSection.Padding   = new Padding(8, 4, 8, 4);
        _currentToolSection.Margin    = new Padding(8, 4, 8, 4);
        _currentToolSectionLabel.Location = new Point(0, 0);
        _currentToolSectionLabel.Width    = FormMinWidth;
        _currentToolSectionLabel.Anchor   = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        var ctLabelH = _currentToolSectionLabel.Height;
        _currentToolLabel.Location  = new Point(0, ctLabelH + 2);
        _currentToolLabel.Width     = FormMinWidth;
        _currentToolLabel.Anchor    = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        _currentToolSection.Controls.Add(_currentToolSectionLabel);
        _currentToolSection.Controls.Add(_currentToolLabel);

        // LastPrompt section — includes a 2 px vertical border-left line (mockup border-left style)
        // drawn via the panel's Paint event at x=0 from top to bottom.
        _lastPromptSection.Dock      = DockStyle.Fill;
        _lastPromptSection.BackColor = BgForm;
        // Left padding 10 makes room for the 2px border-left line + gap.
        _lastPromptSection.Padding   = new Padding(10, 4, 8, 4);
        _lastPromptSection.Margin    = new Padding(8, 4, 8, 4);
        _lastPromptSection.Paint += (_, pe) =>
        {
            // Use a slightly higher alpha (80) than the Border constant (20) so the
            // 2px line is visible in static PNG renders (DrawToBitmap renders alpha
            // over a pre-composited surface, so very-low-alpha lines disappear).
            using var pen = new Pen(Color.FromArgb(80, 255, 255, 255), 2f);
            pe.Graphics.DrawLine(pen, 0, 0, 0, _lastPromptSection.Height);
        };
        _lastPromptSectionLabel.Location = new Point(0, 0);
        _lastPromptSectionLabel.Width    = FormMinWidth;
        _lastPromptSectionLabel.Anchor   = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        var lpLabelH = _lastPromptSectionLabel.Height;
        _lastPromptLabel.Location  = new Point(0, lpLabelH + 2);
        _lastPromptLabel.Width     = FormMinWidth;
        _lastPromptLabel.Anchor    = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        _lastPromptSection.Controls.Add(_lastPromptSectionLabel);
        _lastPromptSection.Controls.Add(_lastPromptLabel);

        // Sparkline section
        _sparklineSection.Dock      = DockStyle.Fill;
        _sparklineSection.BackColor = BgForm;
        _sparklineSection.Padding   = new Padding(8, 4, 8, 4);
        _sparklineSection.Margin    = new Padding(8, 4, 8, 4);
        _sparklineSectionLabel.Location = new Point(0, 0);
        _sparklineSectionLabel.Width    = FormMinWidth;
        _sparklineSectionLabel.Anchor   = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        var spLabelH = _sparklineSectionLabel.Height;
        _sparkline.Location  = new Point(0, spLabelH + 2);
        _sparkline.Width     = FormMinWidth;
        _sparkline.Height    = 30;
        _sparkline.Anchor    = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom;
        _sparklineSection.Controls.Add(_sparklineSectionLabel);
        _sparklineSection.Controls.Add(_sparkline);

        // Chips section
        _chipsSection.Dock      = DockStyle.Fill;
        _chipsSection.BackColor = BgForm;
        _chipsSection.Padding   = new Padding(8, 4, 8, 4);
        _chipsSection.Margin    = new Padding(8, 4, 8, 4);
        _chipsSectionLabel.Location = new Point(0, 0);
        _chipsSectionLabel.Width    = FormMinWidth;
        _chipsSectionLabel.Anchor   = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        var chLabelH = _chipsSectionLabel.Height;
        _chipsPanel.Location  = new Point(0, chLabelH + 2);
        _chipsPanel.Width     = FormMinWidth;
        _chipsPanel.Anchor    = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        _chipsSection.Controls.Add(_chipsSectionLabel);
        _chipsSection.Controls.Add(_chipsPanel);

        // TableLayoutPanel: 9 rows of Absolute heights.
        // Dock=Fill rows auto-size to their TLP row allocation.
        // AutoSize = true lets the panel (and form) size to the sum of row heights.
        // Do NOT set Dock=Fill on the TableLayoutPanel — Fill competes with AutoSize.
        _tableLayout = new TableLayoutPanel
        {
            AutoSize     = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount  = 1,
            RowCount     = 9,
            BackColor    = BgForm,
            Padding      = Padding.Empty,
            Margin       = Padding.Empty,
            Left         = 0,
            Top          = 0,
            Width        = FormMinWidth,
            Anchor       = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
        };

        _tableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        // Row 0: Fleet strip — Absolute 40px (always visible; top cushion clears 14px corner Region)
        _tableLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        // Row 1: Session header — Absolute 78px (always visible)
        _tableLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78f));
        // Row 2: Status row — Absolute 36px (always visible)
        _tableLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
        // Row 3: Permission bar — 0 when hidden; HeightPermission(62) when visible
        _tableLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0f));
        // Row 4: Current tool — 0 when hidden; HeightCurrentTool(72) when visible
        _tableLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0f));
        // Row 5: Last prompt — 0 when hidden; HeightLastPrompt(72) when visible
        _tableLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0f));
        // Row 6: Sparkline — Absolute 66px (always visible; iter-7: bumped for section label)
        _tableLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66f));
        // Row 7: Chips strip — 0 when no chips; HeightChips(62) when visible
        _tableLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0f));
        // Row 8: Footer — Absolute 38px (always visible)
        _tableLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));

        var headerPanel = BuildHeaderPanel();
        var statusRow   = BuildStatusRow();

        _tableLayout.Controls.Add(_fleetStrip,          0, RowFleet);
        _tableLayout.Controls.Add(headerPanel,          0, RowHeader);
        _tableLayout.Controls.Add(statusRow,            0, RowStatus);
        _tableLayout.Controls.Add(_permissionBar,       0, RowPermission);
        _tableLayout.Controls.Add(_currentToolSection,  0, RowCurrentTool);
        _tableLayout.Controls.Add(_lastPromptSection,   0, RowLastPrompt);
        _tableLayout.Controls.Add(_sparklineSection,    0, RowSparkline);
        _tableLayout.Controls.Add(_chipsSection,        0, RowChips);
        _tableLayout.Controls.Add(_footer,              0, RowFooter);

        Controls.Add(_tableLayout);

        ResumeLayout(false);
    }

    private Panel BuildHeaderPanel()
    {
        var panel = new Panel
        {
            Dock      = DockStyle.Fill,
            BackColor = BgForm,
            Margin    = new Padding(8, 4, 8, 4),
        };

        _accentBar.Height = panel.Height;
        panel.Controls.Add(_accentBar);

        // nameRow and subtitleRow use Anchor=Left|Right|Top so they track panel width on resize.
        // Width is seeded at FormMinWidth - Left(12) - rightMargin(8) so first render is correct.
        var innerWidth = FormMinWidth - 12 - 8;

        // Session name: cap at 300px so it never fills the nameRow and crowds out other content.
        // AutoEllipsis requires AutoSize=false (they are mutually exclusive in WinForms).
        // Width is set to the cap — short names leave blank space but truncation always works.
        _sessionNameLabel.AutoSize     = false;
        _sessionNameLabel.AutoEllipsis = true;
        _sessionNameLabel.Width        = 300;
        _sessionNameLabel.Height       = 26;

        var nameRow = new FlowLayoutPanel
        {
            Left          = 12,
            Top           = 16,
            Width         = innerWidth,
            Height        = 28, // 15pt Bold descenders need ~26px; 28 gives safe margin
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents  = false,
            BackColor     = Color.Transparent,
            Anchor        = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
        };
        nameRow.Controls.Add(_sessionNameLabel);
        // _personaChip intentionally excluded from layout until persona feature lands (decisions.md D5)

        // Project label: cap at 160px so cwd + Desktop chip always fit on the subtitle row.
        _projectLabel.AutoSize     = false;
        _projectLabel.AutoEllipsis = true;
        _projectLabel.Width        = 160;
        _projectLabel.Height       = 18;

        // cwd label: already truncated by FrontTruncatePath; cap at 200px so Desktop chip always shows.
        _cwdLabel.AutoSize     = false;
        _cwdLabel.AutoEllipsis = true;
        _cwdLabel.Width        = 200;
        _cwdLabel.Height       = 18;

        var subtitleRow = new FlowLayoutPanel
        {
            Left          = 12,
            Top           = 44, // 16 (nameRow.Top) + 28 (nameRow.Height)
            Width         = innerWidth,
            Height        = 20,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents  = false,
            BackColor     = Color.Transparent,
            Anchor        = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
        };
        _projectLabel.Margin = new Padding(0, 0, 6, 0);
        _cwdLabel.Margin     = new Padding(0, 0, 8, 0);
        _desktopChip.BackColor = Color.FromArgb(15, 255, 255, 255);
        _desktopChip.Padding   = new Padding(5, 1, 5, 1);
        _desktopChip.Paint += (_, pe) =>
        {
            // Draw a 1 px border in the Border color. Alpha 80 so it's visible in static PNGs
            // (DrawToBitmap pre-composites alpha; very-low-alpha lines disappear per iter-7 discovery).
            var r = new Rectangle(0, 0, _desktopChip.Width - 1, _desktopChip.Height - 1);
            using var pen = new Pen(Color.FromArgb(80, 255, 255, 255), 1f);
            pe.Graphics.DrawRectangle(pen, r);
        };
        subtitleRow.Controls.Add(_projectLabel);
        subtitleRow.Controls.Add(_cwdLabel);
        subtitleRow.Controls.Add(_desktopChip);

        panel.Controls.Add(nameRow);
        panel.Controls.Add(subtitleRow);
        return panel;
    }

    private Panel BuildStatusRow()
    {
        var row = new FlowLayoutPanel
        {
            Dock          = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents  = false,
            BackColor     = BgForm,
            Padding       = new Padding(14, 4, 14, 0),
            Margin        = new Padding(8, 4, 8, 4),
        };

        // Status pill dot: 6×6 px circular indicator, vertically centered in the row.
        // The dot is positioned inside a containing Panel that matches the pill height
        // so the FlowLayoutPanel vertical-alignment works without manual Top calculation.
        var dotContainer = new Panel
        {
            Width     = 6,
            Height    = 22,
            BackColor = Color.Transparent,
            Margin    = new Padding(0, 0, 6, 0),
        };
        _statusPillDot.Left = 0;
        _statusPillDot.Top  = 8; // vertically center 6 px dot in 22 px container
        dotContainer.Controls.Add(_statusPillDot);

        _statusPill.Padding  = new Padding(8, 2, 8, 2);
        _statusPill.Margin   = new Padding(0, 0, 8, 0);
        _elapsedLabel.Margin    = new Padding(0, 0, 10, 0);
        _turnCountLabel.Margin  = new Padding(0, 0, 10, 0);
        _sessionAgeLabel.Margin = new Padding(0, 0, 0, 0);

        row.Controls.Add(dotContainer);
        row.Controls.Add(_statusPill);
        row.Controls.Add(_elapsedLabel);
        row.Controls.Add(_turnCountLabel);
        row.Controls.Add(_sessionAgeLabel);
        return row;
    }

    private void LayoutFleetStrip()
    {
        _fleetStrip.Controls.Clear();

        // Inner-content positions compensate for the +8/+4 we removed from the
        // panel's outer Margin (so visual content lands where the cushion fix put it).
        _fleetLabel.Left   = 18;
        _fleetLabel.Top    = 18;
        _fleetLabel.Width  = 30;
        _fleetLabel.Height = 14;
        _fleetStrip.Controls.Add(_fleetLabel);

        // fleetDotsPanel + fleetCount: Anchor seeds are unreliable here because the
        // parent's final Width isn't known until a Resize fires; RepositionFleetStripChildren
        // (wired at construction) flush-rights the count and caps the dots panel to fit.
        _fleetDotsPanel.Left   = 52;
        _fleetDotsPanel.Top    = 18;
        _fleetDotsPanel.Height = 10;
        _fleetDotsPanel.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        _fleetStrip.Controls.Add(_fleetDotsPanel);

        _fleetCount.Top    = 16;
        _fleetCount.Width  = 40;
        _fleetCount.Height = 14;
        _fleetCount.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _fleetCount.TextAlign = ContentAlignment.MiddleRight;
        _fleetStrip.Controls.Add(_fleetCount);
    }

    /// <summary>
    /// Flush-rights <see cref="_fleetCount"/> against the strip's right edge and caps
    /// <see cref="_fleetDotsPanel"/>.Width so it never overruns the count Label.
    /// Anchor alone is unreliable here because the strip's final Width isn't known at
    /// LayoutFleetStrip-call time. Wired to <see cref="_fleetStrip"/>.Resize and called
    /// once at the end of <see cref="UpdateFleetStrip"/> so it also adapts when the
    /// count text width changes (AutoSize).
    /// </summary>
    private void RepositionFleetStripChildren()
    {
        const int RightGap = 14;
        const int DotsGap  = 8;
        var stripWidth = _fleetStrip.ClientSize.Width;
        if (stripWidth <= 0)
            return;
        _fleetCount.Left = stripWidth - _fleetCount.Width - RightGap;
        _fleetDotsPanel.Width = Math.Max(0, _fleetCount.Left - _fleetDotsPanel.Left - DotsGap);
    }

    private void LayoutFooter()
    {
        _footer.Controls.Clear();

        // Two-column footer layout:
        //   Left column (AutoSize): git chip + subagents + failure — truncate when long
        //   Right column (Fill):    keyboard hints — always flush-right, never clipped
        // A TableLayoutPanel with two columns is the correct fix; a single FlowLayoutPanel
        // with WrapContents=false allowed the git label to squeeze keyboard hints off screen.
        var footerTlp = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 2,
            RowCount    = 1,
            BackColor   = BgFooter,
            Padding     = new Padding(22, 9, 22, 9),
            Margin      = Padding.Empty,
        };
        footerTlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));  // left: takes remaining
        footerTlp.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));         // right: keyboard hints
        footerTlp.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        // Left: git + subagents + failure — horizontal FlowLayoutPanel, overflow clips into right padding
        var leftFlow = new FlowLayoutPanel
        {
            Dock          = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents  = false,
            BackColor     = Color.Transparent,
            Padding       = Padding.Empty,
            Margin        = Padding.Empty,
        };
        _gitLabel.AutoSize      = false;
        _gitLabel.AutoEllipsis  = true;
        _gitLabel.Width         = 180; // cap git branch at 180px; ellipsis handles overflow
        _gitLabel.MaximumSize   = new Size(180, 0);
        _gitLabel.Margin        = new Padding(0, 0, 10, 0);
        _subagentsLabel.Margin  = new Padding(0, 0, 10, 0);
        _failureLabel.Margin    = new Padding(0, 0, 0, 0);
        leftFlow.Controls.Add(_gitLabel);
        leftFlow.Controls.Add(_subagentsLabel);
        leftFlow.Controls.Add(_failureLabel);

        // Right: keyboard hints — AutoSize column; no Dock override needed since the
        // FlowLayoutPanel already has AutoSize=true. Anchor to the top of the cell.
        _keyboardHintsLabel.Margin = Padding.Empty;

        footerTlp.Controls.Add(leftFlow,             0, 0);
        footerTlp.Controls.Add(_keyboardHintsLabel,  1, 0);
        _footer.Controls.Add(footerTlp);
    }

    private void UpdateFleetStrip(DashboardViewModel vm, DateTimeOffset now)
    {
        foreach (Control c in _fleetDotsPanel.Controls) c.Dispose();
        _fleetDotsPanel.Controls.Clear();
        var x = 0;
        for (var i = 0; i < vm.FleetItems.Count; i++)
        {
            var item = vm.FleetItems[i];
            var (r, g, b) = StatusMap.ResolveColor(item.Status);
            var accent = Color.FromArgb(r, g, b);

            if (item.IsHovered)
            {
                // Focused item: 18×8 rounded rectangle with subtle outer ring beneath.
                // Two panels stacked: outer ring (22×12, transparent bg, ring drawn in Paint)
                // and inner pill (18×8, drawn as rounded rect via Paint).
                // For simplicity we use one panel with a composite Paint that draws both.
                var pill = new Panel
                {
                    Width     = 22,
                    Height    = 12,
                    Left      = x,
                    Top       = -1, // vertically center 12 px in 10 px dot area
                    BackColor = Color.Transparent,
                };
                var capturedAccent = accent;
                pill.Paint += (_, pe) =>
                {
                    pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    // Outer ring: 2 px wider rounded rect at ~25% opacity
                    var ringColor = Color.FromArgb(64, capturedAccent.R, capturedAccent.G, capturedAccent.B);
                    DrawRoundedRect(pe.Graphics, new Rectangle(0, 0, 21, 11), 5, ringColor, fill: false, strokeWidth: 1.5f);
                    // Inner pill: 18×8 filled rounded rect
                    DrawRoundedRect(pe.Graphics, new Rectangle(2, 2, 17, 7), 4, capturedAccent, fill: true, strokeWidth: 0f);
                };
                _fleetDotsPanel.Controls.Add(pill);
                x += pill.Width + 4;
            }
            else
            {
                // Non-focused items: 8×8 circular dot
                var dot = new Panel
                {
                    Width     = 8,
                    Height    = 8,
                    Left      = x,
                    Top       = 1,
                    BackColor = Color.Transparent,
                };
                var capturedAccent = accent;
                dot.Paint += (_, pe) =>
                {
                    pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    using var b = new SolidBrush(capturedAccent);
                    pe.Graphics.FillEllipse(b, 0, 0, dot.Width - 1, dot.Height - 1);
                };
                _fleetDotsPanel.Controls.Add(dot);
                x += dot.Width + 5;
            }
        }

        var total = vm.FleetItems.Count;
        int hoveredIndex = -1;
        for (var i = 0; i < total; i++)
        {
            if (vm.FleetItems[i].IsHovered) { hoveredIndex = i; break; }
        }

        if (total > 1 && hoveredIndex >= 0)
            _fleetCount.Text = $"{hoveredIndex + 1} / {total}";
        else if (total == 1)
            _fleetCount.Text = "1 / 1";
        else
            _fleetCount.Text = "";

        // Re-flush count after AutoSize may have changed its Width.
        RepositionFleetStripChildren();
    }

    /// <summary>
    /// Draws a rounded rectangle on the given graphics surface.
    /// When fill=true, fills the interior with the given color.
    /// When fill=false, strokes the border with strokeWidth and the given color.
    /// </summary>
    private static void DrawRoundedRect(Graphics g, Rectangle rect, int radius, Color color,
        bool fill, float strokeWidth)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;
        var diameter = radius * 2;
        using var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        if (fill)
        {
            using var b = new SolidBrush(color);
            g.FillPath(b, path);
        }
        else
        {
            using var pen = new Pen(color, strokeWidth);
            g.DrawPath(pen, path);
        }
    }

    private void UpdateStatusPill(string status)
    {
        var (r, g, b) = StatusMap.ResolveColor(status);
        var accent = Color.FromArgb(r, g, b);
        _statusPill.Text      = status.ToUpperInvariant();
        _statusPill.ForeColor = accent;
        _statusPill.BackColor = Color.FromArgb(40, r, g, b);
        // Keep the circular dot color in sync with the pill accent color.
        _statusPillDot.BackColor = accent;
        _statusPillDot.Invalidate();
    }

    // Maximum number of tool chips to render before appending a "+N more" overflow chip.
    // At 520 px width with ~60px per chip, 8 chips fill the row; cap at 8 and show overflow.
    private const int MaxVisibleChips = 8;

    private void UpdateChips(IReadOnlyList<RecentToolEntry> tools)
    {
        foreach (Control c in _chipsPanel.Controls) c.Dispose();
        _chipsPanel.Controls.Clear();

        if (tools.Count == 0)
        {
            SetRowVisible(RowChips, false, HeightChips);
            return;
        }

        var visibleCount = Math.Min(tools.Count, MaxVisibleChips);
        var chipAdded = false;
        for (var i = 0; i < visibleCount; i++)
        {
            var toolName = tools[i].ToolName;
            if (string.IsNullOrEmpty(toolName)) continue;
            var (bg, fg) = ResolveToolChipColors(toolName);
            var chip = new Label
            {
                Text        = toolName,
                Font        = new Font("Consolas", 9f),
                ForeColor   = fg,
                BackColor   = bg,
                AutoSize    = true,
                Padding     = new Padding(5, 2, 5, 2),
                Margin      = new Padding(0, 0, 4, 0),
                BorderStyle = BorderStyle.None,
            };
            _chipsPanel.Controls.Add(chip);
            chipAdded = true;
        }

        var overflow = tools.Count - visibleCount;
        if (overflow > 0)
        {
            var overflowChip = new Label
            {
                Text        = $"+{overflow} more",
                Font        = new Font("Consolas", 9f),
                ForeColor   = FgMuted,
                BackColor   = Color.FromArgb(12, 255, 255, 255),
                AutoSize    = true,
                Padding     = new Padding(5, 2, 5, 2),
                Margin      = new Padding(0, 0, 0, 0),
                BorderStyle = BorderStyle.None,
            };
            _chipsPanel.Controls.Add(overflowChip);
            chipAdded = true;
        }

        // Show the row only if at least one chip was rendered — guards against a list of
        // entirely null/empty ToolName entries that would otherwise leave a visible empty gap.
        SetRowVisible(RowChips, chipAdded, HeightChips);
    }

    /// <summary>
    /// Shows or hides an optional TableLayoutPanel row by toggling its row height.
    /// Setting height to 0 collapses the row; setting to the control's preferred height shows it.
    /// This is the canonical WinForms pattern for conditional rows in a TableLayoutPanel.
    /// </summary>
    private void SetRowVisible(int rowIndex, bool visible, int height)
    {
        _tableLayout.RowStyles[rowIndex] = new RowStyle(SizeType.Absolute, visible ? height : 0f);
    }

    // ---- Static helpers ----

    private static Label MakeLabel(string text, float fontSize, Color foreColor, bool bold)
        => new()
        {
            Text      = text,
            Font      = new Font("Segoe UI", fontSize, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = foreColor,
            BackColor = Color.Transparent,
            AutoSize  = true,
            Padding   = Padding.Empty,
            Margin    = Padding.Empty,
        };

    /// <summary>
    /// Creates a section label — small uppercase muted text above a content block.
    /// AutoSize=true and Anchor=Top|Left so it sizes to text only; parent anchoring
    /// sets width. Margin=(0,0,0,4) for inter-section gap when stacked.
    /// </summary>
    private static Label MakeSectionLabel(string text)
        => new()
        {
            Text      = text,
            Font      = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = FgMuted,
            BackColor = Color.Transparent,
            AutoSize  = true,
            Anchor    = AnchorStyles.Top | AnchorStyles.Left,
            Padding   = Padding.Empty,
            Margin    = new Padding(0, 0, 0, 4),
        };

    private static Color StatusColor(string status)
    {
        var (r, g, b) = StatusMap.ResolveColor(status);
        return Color.FromArgb(r, g, b);
    }

    private static string FrontTruncatePath(string path, int maxChars)
    {
        if (path.Length <= maxChars) return path;
        return "…" + path[^(maxChars - 1)..];
    }

    /// <summary>
    /// Formats a duration as a compact human-readable string without trailing unit:
    /// "18s" | "2m" | "1h 14m" | "3d". Callers add contextual prefixes/suffixes
    /// (e.g. "for 18s", "idle 2m ago", "42m old").
    /// </summary>
    private static string FormatDuration(TimeSpan span)
    {
        if (span.TotalSeconds < 60) return $"{(int)span.TotalSeconds}s";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m";
        var h = (int)span.TotalHours;
        var m = (int)(span.TotalMinutes % 60);
        if (h < 24) return m > 0 ? $"{h}h {m}m" : $"{h}h";
        return $"{(int)span.TotalDays}d";
    }

    /// <summary>
    /// Returns the (background, foreground) color tuple for a tool chip per the canonical
    /// tool color mapping table in decisions.md D4. Unmatched tools (e.g. MCP tools) get the
    /// neutral fallback. Case-insensitive.
    /// </summary>
    private static (Color background, Color foreground) ResolveToolChipColors(string toolName)
        => toolName.ToLowerInvariant() switch
        {
            "read"             => (Color.FromArgb(38, 90, 158, 176), Color.FromArgb(124, 191, 209)),
            "edit"  or "write" => (Color.FromArgb(38, 139, 176, 90), Color.FromArgb(169, 201, 125)),
            "bash"             => (Color.FromArgb(38, 227, 139, 75),  Color.FromArgb(235, 176, 135)),
            "grep"  or "glob"  => (Color.FromArgb(31, 212, 165, 85),  Color.FromArgb(212, 184, 119)),
            "task"  or "agent" => (Color.FromArgb(38, 167, 127, 200), Color.FromArgb(196, 167, 224)),
            _                  => (Color.FromArgb(12, 255, 255, 255), Color.FromArgb(155, 161, 173)),
        };

    /// <summary>
    /// Builds the kbd-styled keyboard hints row for the footer.
    /// Each key glyph is a small Label with a subtle border painted via OnPaint.
    /// Plain separator text sits between key labels with no border.
    /// </summary>
    private static FlowLayoutPanel MakeKbdHintsFlow()
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
            System.Diagnostics.Debug.WriteLine($"DashboardForm: Cascadia Code font unavailable; falling back to Consolas. {ex.Message}");
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
    private static Label MakeKbdKey(string text, Font font)
    {
        var lbl = new Label
        {
            Text      = text,
            Font      = font,
            ForeColor = FgSecondary,
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
    private static Label MakeKbdSeparator(string text)
        => new()
        {
            Text      = text,
            Font      = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = FgMuted,
            BackColor = Color.Transparent,
            AutoSize  = true,
            Padding   = Padding.Empty,
            Margin    = new Padding(0, 2, 0, 0),
        };
}
