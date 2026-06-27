using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Imrdy.Core;
using Imrdy.Core.Desktop;
using Imrdy.Core.Display;
using Imrdy.Core.Graphics;
using Imrdy.Core.Status;
using Imrdy.Windows.Desktop;
using Imrdy.Windows.Icons;
using Imrdy.Windows.Interaction;
using Imrdy.Windows.Theme;
using Microsoft.Extensions.Logging;
using Svg;

namespace Imrdy.Windows.Overlay;

/// <summary>
/// Single non-layered, DWM-composited, always-interactive overlay panel.
/// Replaces OverlayWindowBase, InteractiveOverlayWindow, and PassiveOverlayWindow
/// (all three deleted by the redesign — this is the one surviving surface).
/// Edge-docked, top-most WinForms Form that renders status chips via OnPaint.
/// Pinned to all virtual desktops; re-pins on TaskbarCreated (Explorer restart)
/// via the registered window message (Decision 9).
/// The panel has no layered or click-through extended styles — it must stay activatable
/// so right-click transfers foreground for context-menu hover hot-track (Decision 3, 11).
/// </summary>
internal sealed class OverlayPanel : Form
{
    // ── Layout constants (Decision 10) ────────────────────────────────────────────
    // Hardcoded, not config fields — mirrors the dashboards' fixed 14 px radius precedent.
    internal const int ChipPadding      = 6;
    internal const int ChipCornerRadius = 6;
    internal const int PanelPadding     = 4;

    // Depends on _config.Size (runtime value) — cannot be const.
    private int MinimumPanelWidth => 2 * PanelPadding + _config.Size;

    // ── TaskbarCreated registration ───────────────────────────────────────────────
    // RegisterWindowMessage returns 0 on failure (atom-table exhausted — rare).
    // The WndProc guard checks for 0 first to prevent matching WM_NULL (0x0000).
    private static readonly uint _wmTaskbarCreated =
        PInvokeOverlay.RegisterWindowMessage("TaskbarCreated");

    // ── Fields ────────────────────────────────────────────────────────────────────
    private readonly OverlayConfig _config;
    private readonly ISessionInteractionRouter _router;
    private readonly IDesktopManager? _desktopManager;
    private readonly ILogger _logger;
    private readonly GraphicsPackLoader _graphicsPackLoader;

    // Both declared non-nullable and MUST be ctor-initialized to satisfy CS8618
    // under Nullable=enable + TreatWarningsAsErrors.
    // _items: OnPaint fires (on first Show) before any UpdateItems/LoadFixtureItems call.
    // _cache: non-nullable field; disposal contract established here; populated via GetOrCreateBitmap.
    private IReadOnlyList<DisplayItem> _items;
    private Dictionary<(string, string), Bitmap> _cache;

    // Chip container base color (slightly lighter than BgForm to provide a visible
    // chip boundary against the dark panel background).
    // Seed value — tuned visually in step 10.
    private static readonly Color _chipBgBase = Color.FromArgb(50, 52, 66);

    // Nullable — no initial hover target; set by controller via SetHoveredChipId.
    private string? _hoveredChipId;

    // ── Events ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised after a left-click dispatches a session/workspace activation.
    /// Hover dashboard controllers subscribe to dismiss their forms after navigation.
    /// Right-click does not raise this event — menu dismissal is handled by WinForms.
    /// </summary>
    public event Action? SurfaceInteracted;

    // ── Constructor ───────────────────────────────────────────────────────────────

    public OverlayPanel(
        OverlayConfig config,
        ISessionInteractionRouter router,
        IDesktopManager? desktopManager,
        ILoggerFactory loggerFactory,
        GraphicsPackLoader graphicsPackLoader)
    {
        _config            = config;
        _router            = router;
        _desktopManager    = desktopManager;
        _logger            = loggerFactory.CreateLogger<OverlayPanel>();
        _graphicsPackLoader = graphicsPackLoader;

        // Ctor-init both non-nullable fields (CS8618).
        _items = Array.Empty<DisplayItem>();
        _cache = new Dictionary<(string, string), Bitmap>();

        // Form shell
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar   = false;
        TopMost         = true;
        StartPosition   = FormStartPosition.Manual;
        Cursor          = Cursors.Hand;
        DoubleBuffered  = true;

        // Flicker-free triple (spec §Constraints): DoubleBuffered alone does not suppress
        // the WM_ERASEBKGND flash — the full triple is required.
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint            |
            ControlStyles.OptimizedDoubleBuffer,
            true);

        // Solid fallback when DWM mica is unavailable (Win10 ≤19045 or DWM off).
        BackColor = ImrdyPalette.BgForm;
    }

    // ── CreateParams ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds WS_EX_TOOLWINDOW only — no layered, transparent, or non-activatable styles.
    /// The panel must remain activatable so right-click transfers foreground for the
    /// context-menu's hover hot-track pipeline (Decision 3, 11).
    /// MUST start from base.CreateParams — omitting it discards ShowInTaskbar=false
    /// and other styles WinForms bakes into the base ExStyle.
    /// </summary>
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= PInvokeOverlay.WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    // ── Form overrides ────────────────────────────────────────────────────────────

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ImrdyPalette.ApplyMica(this);
        ImrdyPalette.ApplyRoundedRegion(this);
        PinAcrossVirtualDesktops();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        // Region clip must track every size change or the rounded-corner shape becomes stale.
        ImrdyPalette.ApplyRoundedRegion(this);
    }

    /// <summary>
    /// No-op override — part of the flicker-free triple. Suppresses the WM_ERASEBKGND
    /// flash. OnPaint handles all painting; no base call.
    /// </summary>
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Intentional no-op: base call would flash the background erase before OnPaint.
    }

    /// <summary>
    /// Rendering Contract (spec §Rendering Contract / State × Tier Matrix).
    /// Pure function of (_items, _hoveredChipId, config). Geometry from the same slot
    /// math HitIconIndex uses — hit-test and paint can never diverge.
    /// Per chip (L→R, Spacing between, PanelPadding inset):
    ///   1. Chip background — rounded rect at tier-driven alpha (aging visual).
    ///   2. Status glyph — from (style,status) cache, full alpha tiers 0–3, ~85% tier4.
    ///   3. Alert cue — non-color outline for error/permission (Decision 2d).
    ///   4. Hover highlight — controller-pushed via SetHoveredChipId.
    /// Empty state: single dimmed placeholder; panel never zero-width/invisible (Decision 6).
    /// </summary>
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(ImrdyPalette.BgForm);

        var items = _items;

        // Decision 6: zero sessions → single dimmed placeholder, panel stays visible.
        if (items.Count == 0)
        {
            PaintPlaceholderChip(g);
            return;
        }

        var size    = _config.Size;
        var spacing = _config.Spacing;

        for (var i = 0; i < items.Count; i++)
        {
            var item  = items[i];
            var chipX = PanelPadding + i * (size + spacing);
            PaintChip(g, chipX, PanelPadding, size, item);
        }
    }

    private void PaintChip(Graphics g, int chipX, int chipY, int size, DisplayItem item)
    {
        var status   = item.Status;
        var tier     = item.AgingTier;
        var isAlert  = IsAlertStatus(status);
        var chipRect = new Rectangle(chipX, chipY, size, size);

        // ── 1. Chip background (tier-driven opacity fade = aging visual) ──────────
        var chipAlpha = ChipBgAlpha(tier, isAlert);
        using var bgPath  = BuildRoundedRect(chipRect, ChipCornerRadius);
        using var bgBrush = new SolidBrush(
            Color.FromArgb(chipAlpha, _chipBgBase.R, _chipBgBase.G, _chipBgBase.B));
        g.FillPath(bgBrush, bgPath);

        // ── 2. Status glyph (from (style,status) cache, inset by ChipPadding) ────
        var glyphSize = size - 2 * ChipPadding;
        if (glyphSize > 0)
        {
            var glyph     = GetOrCreateBitmap(item.IconStyle, status);
            var glyphRect = new Rectangle(chipX + ChipPadding, chipY + ChipPadding, glyphSize, glyphSize);

            if (tier <= 3)
            {
                // Aging expressed via chip-bg opacity only — glyph at full alpha.
                g.DrawImage(glyph, glyphRect);
            }
            else
            {
                // tier4: slight composite dim (≈85% alpha) per spec.
                using var ia = new ImageAttributes();
                var cm       = new ColorMatrix();
                cm.Matrix33  = 0.85f;
                ia.SetColorMatrix(cm);
                g.DrawImage(glyph, glyphRect, 0, 0, glyph.Width, glyph.Height, GraphicsUnit.Pixel, ia);
            }
        }

        // ── 3. Redundant non-color cue for error/permission (Decision 2d) ────────
        if (isAlert)
            PaintAlertCue(g, chipRect);

        // ── 4. Hover highlight ───────────────────────────────────────────────────
        if (item.Id == _hoveredChipId)
            PaintHoverHighlight(g, chipRect);
    }

    private void PaintPlaceholderChip(Graphics g)
    {
        // Single dimmed imrdy-glyph placeholder for zero-session empty state (Decision 6).
        // Panel stays visible at MinimumPanelWidth. Seed values tuned in step 10.
        const int   placeholderChipAlpha  = 50;
        const float placeholderGlyphAlpha = 0.30f;

        var size     = _config.Size;
        var chipRect = new Rectangle(PanelPadding, PanelPadding, size, size);

        using var bgPath  = BuildRoundedRect(chipRect, ChipCornerRadius);
        using var bgBrush = new SolidBrush(
            Color.FromArgb(placeholderChipAlpha, _chipBgBase.R, _chipBgBase.G, _chipBgBase.B));
        g.FillPath(bgBrush, bgPath);

        var glyphSize = size - 2 * ChipPadding;
        if (glyphSize > 0)
        {
            // "circles"/"idle" (green circle) at very low opacity → calm "ready, no sessions" feel.
            // The glyph style can be updated to an imrdy brand icon when one is available.
            var glyph     = GetOrCreateBitmap("circles", "idle");
            var glyphRect = new Rectangle(PanelPadding + ChipPadding, PanelPadding + ChipPadding, glyphSize, glyphSize);
            using var ia  = new ImageAttributes();
            var cm        = new ColorMatrix();
            cm.Matrix33   = placeholderGlyphAlpha;
            ia.SetColorMatrix(cm);
            g.DrawImage(glyph, glyphRect, 0, 0, glyph.Width, glyph.Height, GraphicsUnit.Pixel, ia);
        }
    }

    private static void PaintAlertCue(Graphics g, Rectangle chipRect)
    {
        // Non-color redundant cue for error/permission: thin white border outline (Decision 2d).
        // One cue only — no theme system. Appearance tuned via imrdy render (step 10).
        using var path = BuildRoundedRect(chipRect, ChipCornerRadius);
        using var pen  = new Pen(Color.FromArgb(180, 255, 255, 255), 1.5f);
        g.DrawPath(pen, path);
    }

    private static void PaintHoverHighlight(Graphics g, Rectangle chipRect)
    {
        // Subtle white tint fill + ring on hover. Appearance tuned via imrdy render (step 10).
        using var path     = BuildRoundedRect(chipRect, ChipCornerRadius);
        using var fillBrush = new SolidBrush(Color.FromArgb(30, 255, 255, 255));
        using var ringPen   = new Pen(Color.FromArgb(80, 255, 255, 255), 1.0f);
        g.FillPath(fillBrush, path);
        g.DrawPath(ringPen, path);
    }

    private static GraphicsPath BuildRoundedRect(Rectangle rect, int radius)
    {
        // Clamp diameter so arcs don't degenerate on very small chips.
        var d    = Math.Min(2 * radius, Math.Min(rect.Width, rect.Height));
        var path = new GraphicsPath();
        path.AddArc(rect.X,         rect.Y,          d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y,          d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d,   0, 90);
        path.AddArc(rect.X,         rect.Bottom - d, d, d,  90, 90);
        path.CloseFigure();
        return path;
    }

    // Seed alpha ladder (tuned by eye in step 10 via imrdy render).
    private static int ChipBgAlpha(int tier, bool isAlert)
    {
        var alpha = tier switch
        {
            0 => 255,
            1 => 200,
            2 => 160,
            3 => 120,
            _ => 80,   // tier4
        };
        // Raised opacity floor: permission/error remain clearly visible at every tier
        // (Decision 2c — never the faint alpha-30 of the old layered path).
        const int AlertFloor = 160;
        return isAlert ? Math.Max(alpha, AlertFloor) : alpha;
    }

    private static bool IsAlertStatus(string status)
        => string.Equals(status, "permission", StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, "error",      StringComparison.OrdinalIgnoreCase);

    // ── WndProc ───────────────────────────────────────────────────────────────────

    protected override void WndProc(ref Message m)
    {
        // Guard FIRST: RegisterWindowMessage returns 0 on failure (atom-table exhausted).
        // Without this guard, a 0 value would match WM_NULL (0x0000) and fire
        // PinAcrossVirtualDesktops on every no-op message — catastrophic throughput hit.
        if (_wmTaskbarCreated == 0)
        {
            base.WndProc(ref m);
            return;
        }

        // Cast to uint avoids sign-extension; registered message IDs live in 0xC000–0xFFFF.
        if ((uint)m.Msg == _wmTaskbarCreated)
        {
            // Explorer restarted — shell discarded all-desktop pin state; re-pin (Decision 9).
            PinAcrossVirtualDesktops();
        }

        base.WndProc(ref m);
    }

    // ── Public API consumed by controllers + TrayApp ──────────────────────────────

    /// <summary>
    /// Live tray path. Sizes the panel to fit the items, computes placement, and
    /// repaints. Never hides the panel on empty — the empty state renders the idle
    /// placeholder (Decision 6).
    /// </summary>
    public void UpdateItems(IReadOnlyList<DisplayItem> items)
    {
        ApplyItemsAndSize(items);
        this.Location = CalculatePosition();
        Invalidate();
    }

    /// <summary>
    /// Render path only (imrdy render / OverlayRenderer). Sizes the panel and repaints
    /// but skips CalculatePosition — the form stays at the offscreen position set by
    /// the Show pattern and never flashes onto the visible desktop.
    /// </summary>
    internal void LoadFixtureItems(IReadOnlyList<DisplayItem> items)
    {
        ApplyItemsAndSize(items);
        Invalidate();
    }

    /// <summary>
    /// Pushes the controller/drain-owned hover-highlight target into the form.
    /// Invalidates only when the id changes. The form is purely presentational —
    /// it does not poll Cursor.Position or read the clock.
    /// </summary>
    public void SetHoveredChipId(string? id)
    {
        if (_hoveredChipId == id) return;
        _hoveredChipId = id;
        Invalidate();
    }

    /// <summary>
    /// Maps a screen-coordinate point to the session id of the icon under it.
    /// Returns false for gaps, workspace items, or points outside the panel.
    /// </summary>
    public bool TryGetSessionIdAtScreenPoint(Point screenPt, out string sessionId)
    {
        sessionId = string.Empty;

        int cx = screenPt.X, cy = screenPt.Y;
        if (!PInvokeOverlay.ScreenToClientPoint(Handle, ref cx, ref cy))
            return false;

        if (!HitIconIndex(cx, out var index))
            return false;

        if (index < 0 || index >= _items.Count)
            return false;

        var item = _items[index];
        if (item.ItemType != DisplayItemType.Session)
            return false;

        sessionId = item.Id;
        return true;
    }

    /// <summary>
    /// Maps an already-converted client-X coordinate to the DisplayItem at that slot.
    /// Used by HoverDashboardControllerBase-derived controllers whose
    /// TryHitTestForOurDomain has already done the screen→client conversion.
    /// </summary>
    public bool TryHitTestAtClient(int clientX, out DisplayItem? item, out int hitIndex)
    {
        item     = null;
        hitIndex = -1;

        if (!HitIconIndex(clientX, out var index))
            return false;

        if (index < 0 || index >= _items.Count)
            return false;

        item     = _items[index];
        hitIndex = index;
        return true;
    }

    /// <summary>
    /// Disposes every Bitmap in the (style, status) cache, clears it, and repaints.
    /// Called from TrayApp on icon-style change so the next OnPaint rebuilds glyphs
    /// with the new style.
    /// </summary>
    public void InvalidateStyleCache()
    {
        foreach (var bmp in _cache.Values) bmp.Dispose();
        _cache.Clear();
        Invalidate();
    }

    // ── Mouse handling ────────────────────────────────────────────────────────────

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && HitIconIndex(e.X, out var idx) && idx < _items.Count)
        {
            var item = _items[idx];
            try
            {
                if (item.ItemType == DisplayItemType.Session)
                    _router.ActivateSession(item.Id);
                else
                    _router.ActivateWorkspace(item.Id);
                SurfaceInteracted?.Invoke();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OverlayPanel: left-click dispatch failed for {ItemType} {Id}",
                    item.ItemType, item.Id);
            }
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right && HitIconIndex(e.X, out var idx) && idx < _items.Count)
        {
            var item = _items[idx];
            try
            {
                var anchor = MenuAnchor.AtControl(this, e.Location);
                if (item.ItemType == DisplayItemType.Session)
                    _router.OpenSessionMenu(item.Id, anchor);
                else
                    _router.OpenWorkspaceMenu(item.Id, anchor);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OverlayPanel: right-click menu failed for {ItemType} {Id}",
                    item.ItemType, item.Id);
            }
        }
        base.OnMouseUp(e);
    }

    // ── Disposal ──────────────────────────────────────────────────────────────────

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Each cached Bitmap holds a native HBITMAP; dispose all to prevent GDI handle
            // leaks toward the 10,000/process limit (especially critical for render-path
            // multi-run scenarios where one panel is constructed per fixture).
            foreach (var bmp in _cache.Values) bmp.Dispose();
            _cache.Clear();
        }
        base.Dispose(disposing);
    }

    // ── Private helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Stores items, computes chip geometry, and resizes the panel.
    /// Does NOT set Location — callers are responsible for positioning.
    /// </summary>
    private void ApplyItemsAndSize(IReadOnlyList<DisplayItem> items)
    {
        _items = items;
        // Minimum 1 chip slot for the idle placeholder when items is empty (Decision 6).
        var count      = Math.Max(1, items.Count);
        var panelWidth = Math.Max(
            MinimumPanelWidth,
            count * _config.Size + (count - 1) * _config.Spacing + 2 * PanelPadding);
        var panelHeight = _config.Size + 2 * PanelPadding;
        this.Size = new Size(panelWidth, panelHeight);
    }

    /// <summary>
    /// Computes the bottom-edge dock position on the resolved target monitor.
    /// Uses the monitor's WorkingArea so placement is correct on any selected screen.
    /// Adds a reserve when the taskbar is auto-hidden (WorkingArea == Bounds).
    /// Garbage/unknown Position falls back to bottom-right (spec §Error Handling).
    /// </summary>
    private Point CalculatePosition()
    {
        var screen     = ResolveTargetScreen();
        var wa         = screen.WorkingArea;
        const int margin = 16;

        // Auto-hide taskbar detection: WorkingArea equals Bounds when no strip is reserved.
        // Reserve ~8 px so the panel stays above the taskbar pop-up zone.
        var taskbarReserve = wa == screen.Bounds ? 8 : 0;

        var y = wa.Bottom - this.Height - taskbarReserve;
        var x = _config.Position == "bottom-left"
            ? wa.Left  + margin
            : wa.Right - this.Width - margin;

        return new Point(x, y);
    }

    /// <summary>
    /// Returns the Screen the panel should dock to. Falls back to primary (or first)
    /// screen when config.Monitor is out of range.
    /// Null-coalescing on Screen.PrimaryScreen prevents CS8602 under TreatWarningsAsErrors.
    /// </summary>
    private Screen ResolveTargetScreen()
    {
        var screens = Screen.AllScreens;
        if (_config.Monitor >= 0 && _config.Monitor < screens.Length)
            return screens[_config.Monitor];
        return Screen.PrimaryScreen ?? screens[0];
    }

    /// <summary>
    /// Maps a client X coordinate to a chip slot index. Subtracts PanelPadding before
    /// delegating to TryGetItemAtClientPoint so the slot math matches OnPaint geometry:
    /// both use i * (size + spacing) as the chip origin, offset by PanelPadding from
    /// the panel left edge. A click in the left PanelPadding gutter returns false.
    /// </summary>
    private bool HitIconIndex(int clientX, out int index)
    {
        return DisplayItemCollection.TryGetItemAtClientPoint(
            _items, clientX - PanelPadding, _config.Size, _config.Spacing,
            out _, out index);
    }

    private void PinAcrossVirtualDesktops()
    {
        // Null in the render path — pinning skipped via null-conditional.
        // Idempotent; COM-exception absorption lives inside the IDesktopManager impl.
        _desktopManager?.PinWindowToAllDesktops(this.Handle);
    }

    // ── Glyph bitmap cache (ported from OverlayWindowBase) ───────────────────────
    // Key is (style, status) — TIER-INDEPENDENT. Aging is NOT baked into the glyph;
    // it is expressed purely as chip-background opacity in OnPaint (Decision 7).
    // ApplyAgingColorMatrix, layered-window composite, SetBounds defense, and
    // Visible=false-on-empty are all deleted — they belong to the layered-window path.

    private Bitmap GetOrCreateBitmap(string style, string status)
    {
        var key = (style, status);
        if (_cache.TryGetValue(key, out var cached)) return cached;

        Bitmap? bitmap = null;
        try
        {
            bitmap      = RenderBitmap(style, status);
            _cache[key] = bitmap;
            return bitmap;
        }
        catch (Exception ex)
        {
            bitmap?.Dispose();
            _logger.LogWarning(ex,
                "OverlayPanel: failed to render bitmap for style='{Style}' status='{Status}', falling back to circle.",
                style, status);
            var fallback = RenderCircleFallback(status);
            _cache[key]  = fallback;
            return fallback;
        }
    }

    private Bitmap RenderBitmap(string style, string status)
    {
        var size          = _config.Size;
        var shapeDelegate = GetShapeDelegate(style);
        if (shapeDelegate is not null)
            return RenderBuiltInShape(shapeDelegate, status, size);

        if (style.StartsWith("pack:", StringComparison.OrdinalIgnoreCase))
            return RenderFromPack(style, status, size);

        throw new InvalidOperationException($"Unknown icon style '{style}'.");
    }

    // Aging bake-in DROPPED: GetAgingFactorFromTier call and aged* local vars removed.
    // OnPaint expresses aging via chip-background opacity — the glyph always carries the
    // full status color, which makes the cache tier-independent.
    private static Bitmap RenderBuiltInShape(Action<Graphics, RectangleF, Brush> drawDelegate, string status, int size)
    {
        var (r, gv, b) = StatusMap.ResolveColor(status);

        Bitmap? bitmap = null;
        try
        {
            bitmap = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            using var brush = new SolidBrush(Color.FromArgb(r, gv, b));
            var rect = new RectangleF(1, 1, size - 2, size - 2);
            drawDelegate(graphics, rect, brush);
            return bitmap;
        }
        catch
        {
            bitmap?.Dispose();
            throw;
        }
    }

    // Tier-branch DROPPED: no ApplyAgingColorMatrix path; ClonePArgb used unconditionally.
    // (OverlayWindowBase:236-239 tier == 0 ? ClonePArgb : ApplyAgingColorMatrix deleted.)
    private Bitmap RenderFromPack(string style, string status, int size)
    {
        var packName  = style["pack:".Length..];
        var packsRoot = Path.GetFullPath(ImrdyPaths.GraphicsPacksDir);
        var packDir   = Path.GetFullPath(Path.Combine(packsRoot, packName));
        if (!packDir.StartsWith(packsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Invalid pack name '{packName}' — path traversal detected.");

        var packJsonPath = Path.Combine(packDir, "pack.json");
        var pack = _graphicsPackLoader.LoadPack(packDir, packJsonPath)
            ?? throw new InvalidOperationException($"Pack '{packName}' could not be loaded.");

        if (!pack.StateFilePaths.TryGetValue(status, out var filePath))
        {
            if (!pack.StateFilePaths.TryGetValue("unknown", out filePath) &&
                !pack.StateFilePaths.TryGetValue("idle",    out filePath))
            {
                throw new InvalidOperationException($"Pack '{packName}' has no entry for status '{status}'.");
            }
        }

        // SvgDocument does not implement IDisposable in Svg.NET 3.4.7
        var doc = SvgDocument.Open(filePath);
        using var baseBitmap = EnsurePArgb(doc.Draw(size, size), size);

        Bitmap? result = null;
        try
        {
            result = ClonePArgb(baseBitmap, size);
            return result;
        }
        catch
        {
            result?.Dispose();
            throw;
        }
    }

    private Bitmap RenderCircleFallback(string status)
    {
        try
        {
            return RenderBuiltInShape(ShapeDefinitions.Circle, status, _config.Size);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "OverlayPanel: even circle fallback failed for status='{Status}'.", status);
            return new Bitmap(_config.Size, _config.Size, PixelFormat.Format32bppPArgb);
        }
    }

    private static Action<Graphics, RectangleF, Brush>? GetShapeDelegate(string style) => style switch
    {
        "circles"   => ShapeDefinitions.Circle,
        "squares"   => ShapeDefinitions.Square,
        "triangles" => ShapeDefinitions.Triangle,
        "diamonds"  => ShapeDefinitions.Diamond,
        "hexagons"  => ShapeDefinitions.Hexagon,
        "plus"      => ShapeDefinitions.Plus,
        _           => null
    };

    private static Bitmap EnsurePArgb(Bitmap source, int size)
    {
        if (source.PixelFormat == PixelFormat.Format32bppPArgb) return source;
        var converted = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(converted);
        g.DrawImage(source, 0, 0, size, size);
        source.Dispose();
        return converted;
    }

    private static Bitmap ClonePArgb(Bitmap source, int size)
    {
        var clone = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(clone);
        g.DrawImage(source, 0, 0, size, size);
        return clone;
    }
}
