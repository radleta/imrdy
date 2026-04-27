using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Imrdy.Core;
using Imrdy.Core.Display;
using Imrdy.Core.Graphics;
using Imrdy.Core.Icons;
using Imrdy.Core.Status;
using Imrdy.Windows.Desktop;
using Imrdy.Windows.Icons;
using Microsoft.Extensions.Logging;
using Svg;

namespace Imrdy.Windows.Overlay;

/// <summary>
/// Shared base for both overlay variants. Owns bitmap cache, composite rendering,
/// and the bottom-of-screen layered-window positioning. Subclasses differ ONLY in
/// CreateParams (passive adds WS_EX_TRANSPARENT) and whether they handle WndProc
/// input (interactive does, passive doesn't).
///
/// We do NOT run a topmost watchdog. <see cref="Form.TopMost"/> = true is enough;
/// re-asserting HWND_TOPMOST on a timer shoves the overlay above any other topmost
/// window — including an open ContextMenuStrip popup — which clips the menu after
/// every tick. If real-world z-order displacement turns out to be a problem, add
/// recovery at the displacement source, not on a periodic timer.
/// </summary>
internal abstract class OverlayWindowBase : Form
{
    private readonly OverlayConfig _config;
    private readonly GraphicsPackLoader _graphicsPackLoader;
    private readonly Dictionary<(string style, string status, int tier), Bitmap> _cache = new();
    protected readonly ILogger _logger;
    protected IReadOnlyList<DisplayItem> _items = Array.Empty<DisplayItem>();

    private const int MaxVisibleItems = 50;

    protected OverlayWindowBase(OverlayConfig config, ILoggerFactory loggerFactory, GraphicsPackLoader graphicsPackLoader)
    {
        _config = config;
        _logger = loggerFactory.CreateLogger(GetType());
        _graphicsPackLoader = graphicsPackLoader;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= PInvokeOverlay.WS_EX_LAYERED;
            cp.ExStyle |= PInvokeOverlay.WS_EX_TOOLWINDOW;
            // WS_EX_NOACTIVATE is added by PassiveOverlayWindow only. The interactive
            // variant must be activatable so clicks transfer foreground naturally —
            // that's how the popup context menu gets the foreground anchor it needs
            // for hover-tracking (per the Raymond Chen recipe).
            return cp;
        }
    }

    protected int IconSize => _config.Size;
    protected int IconCount => _items.Count;

    /// <summary>
    /// Returns the overlay's actual screen bounds via Win32 GetWindowRect.
    /// Use this instead of <see cref="Form.Bounds"/> — WinForms caches Bounds in
    /// internal fields that only refresh on WM_WINDOWPOSCHANGED, and
    /// UpdateLayeredWindow (which positions this window) does not fire that message
    /// in a way WinForms catches for layered+toolwindow forms. SetBounds() also
    /// fails to refresh the cache in this configuration. Bounds returns the WinForms
    /// default (0,0,300,300) for the entire tray-process life; this property returns
    /// the live OS-truth rect on every read.
    /// Returns <see cref="Rectangle.Empty"/> before the handle is created.
    /// </summary>
    public Rectangle ActualScreenBounds =>
        IsHandleCreated ? PInvokeOverlay.GetActualWindowRect(Handle) : Rectangle.Empty;

    public virtual void UpdateItems(IReadOnlyList<DisplayItem> items)
    {
        _items = items;
        if (items.Count == 0) { Visible = false; return; }

        var visibleCount = Math.Min(items.Count, MaxVisibleItems);
        var totalWidth = visibleCount * _config.Size + (visibleCount - 1) * _config.Spacing;
        using var composite = new Bitmap(totalWidth, _config.Size, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(composite);
        g.Clear(Color.Transparent);

        for (int i = 0; i < visibleCount; i++)
        {
            var item = items[i];
            var bmp = GetOrCreateBitmap(item.IconStyle, item.Status, item.AgingTier);
            var x = i * (_config.Size + _config.Spacing);
            g.DrawImageUnscaled(bmp, x, 0);
        }

        var position = CalculatePosition(totalWidth);

        _logger.LogDebug(
            "Overlay UpdateItems: items={Count}, position={X},{Y}, totalWidth={W}, iconSize={H}, currentBounds={Bounds}, currentVisible={Visible}, handleCreated={HC}",
            items.Count, position.X, position.Y, totalWidth, _config.Size, this.Bounds, this.Visible, this.IsHandleCreated);

        PInvokeOverlay.SetBitmap(Handle, composite, position);

        _logger.LogDebug(
            "Overlay after SetBitmap: GetWindowRect={Rect}",
            PInvokeOverlay.GetActualWindowRect(Handle));

        // Defense-in-depth: try to keep WinForms' cached Bounds close to reality.
        // On layered+toolwindow forms UpdateLayeredWindow does not reliably fire
        // WM_WINDOWPOSCHANGED so the cache stays stale regardless; external code
        // must use ActualScreenBounds (GetWindowRect) rather than Bounds.
        SetBounds(position.X, position.Y, totalWidth, _config.Size);

        _logger.LogDebug(
            "Overlay after SetBounds: Form.Bounds={Bounds}, GetWindowRect={Rect}",
            this.Bounds, PInvokeOverlay.GetActualWindowRect(Handle));

        Visible = true;
    }

    /// <summary>
    /// Maps a client X coordinate to an icon index. Used only by InteractiveOverlayWindow's
    /// WndProc — lives here so both classes share the same slot math without duplication.
    /// </summary>
    protected bool HitIconIndex(int clientX, out int index)
    {
        index = -1;
        if (clientX < 0) return false;
        var slot = _config.Size + _config.Spacing;
        if (slot <= 0) return false;
        var i = clientX / slot;
        var inSlot = clientX % slot;
        if (inSlot >= _config.Size) return false;
        if (i >= _items.Count) return false;
        index = i;
        return true;
    }

    public void InvalidateStyleCache()
    {
        foreach (var bmp in _cache.Values) bmp.Dispose();
        _cache.Clear();
    }

    private Bitmap GetOrCreateBitmap(string style, string status, int tier)
    {
        var key = (style, status, tier);
        if (_cache.TryGetValue(key, out var cached)) return cached;

        Bitmap? bitmap = null;
        try
        {
            bitmap = RenderBitmap(style, status, tier);
            _cache[key] = bitmap;
            return bitmap;
        }
        catch (Exception ex)
        {
            bitmap?.Dispose();
            _logger.LogWarning(ex, "Overlay: failed to render bitmap for style='{Style}' status='{Status}' tier={Tier}, falling back to circle.", style, status, tier);
            var fallback = RenderCircleFallback(status, tier);
            _cache[key] = fallback;
            return fallback;
        }
    }

    private Bitmap RenderBitmap(string style, string status, int tier)
    {
        var size = _config.Size;

        var shapeDelegate = GetShapeDelegate(style);
        if (shapeDelegate is not null)
            return RenderBuiltInShape(shapeDelegate, status, tier, size);

        if (style.StartsWith("pack:", StringComparison.OrdinalIgnoreCase))
            return RenderFromPack(style, status, tier, size);

        throw new InvalidOperationException($"Unknown icon style '{style}'.");
    }

    private static Bitmap RenderBuiltInShape(Action<Graphics, RectangleF, Brush> drawDelegate, string status, int tier, int size)
    {
        var (r, g, b) = StatusMap.ResolveColor(status);
        var factor = StatusMap.GetAgingFactorFromTier(tier);
        var agedR = (byte)(r * factor);
        var agedG = (byte)(g * factor);
        var agedB = (byte)(b * factor);

        Bitmap? bitmap = null;
        try
        {
            bitmap = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            using var brush = new SolidBrush(Color.FromArgb(agedR, agedG, agedB));
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

    private Bitmap RenderFromPack(string style, string status, int tier, int size)
    {
        var packName = style["pack:".Length..];
        var packsRoot = Path.GetFullPath(ImrdyPaths.GraphicsPacksDir);
        var packDir = Path.GetFullPath(Path.Combine(packsRoot, packName));
        if (!packDir.StartsWith(packsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Invalid pack name '{packName}' — path traversal detected.");

        var packJsonPath = Path.Combine(packDir, "pack.json");
        var pack = _graphicsPackLoader.LoadPack(packDir, packJsonPath)
            ?? throw new InvalidOperationException($"Pack '{packName}' could not be loaded.");

        if (!pack.StateFilePaths.TryGetValue(status, out var filePath))
        {
            if (!pack.StateFilePaths.TryGetValue("unknown", out filePath) &&
                !pack.StateFilePaths.TryGetValue("idle", out filePath))
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
            result = tier == 0
                ? ClonePArgb(baseBitmap, size)
                : ApplyAgingColorMatrix(baseBitmap, size, tier);
            return result;
        }
        catch
        {
            result?.Dispose();
            throw;
        }
    }

    private Bitmap RenderCircleFallback(string status, int tier)
    {
        try
        {
            return RenderBuiltInShape(ShapeDefinitions.Circle, status, tier, _config.Size);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Overlay: even circle fallback failed for status='{Status}' tier={Tier}.", status, tier);
            return new Bitmap(_config.Size, _config.Size, PixelFormat.Format32bppPArgb);
        }
    }

    private static Action<Graphics, RectangleF, Brush>? GetShapeDelegate(string style) => style switch
    {
        "circles" => ShapeDefinitions.Circle,
        "squares" => ShapeDefinitions.Square,
        "triangles" => ShapeDefinitions.Triangle,
        "diamonds" => ShapeDefinitions.Diamond,
        "hexagons" => ShapeDefinitions.Hexagon,
        "plus" => ShapeDefinitions.Plus,
        _ => null
    };

    private Point CalculatePosition(int contentWidth)
    {
        var wa = Screen.PrimaryScreen!.WorkingArea;
        var y = wa.Bottom - _config.Size - 16;
        var x = _config.Position == "bottom-left"
            ? wa.Left + 16
            : wa.Right - contentWidth - 16;
        return new Point(x, y);
    }

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

    private static Bitmap ApplyAgingColorMatrix(Bitmap source, int size, int tier)
    {
        var agingScale = 1.0f - (tier / 4.0f);
        var grayOffset = (1.0f - agingScale) * 0.5f;
        var alphaMul = 1.0f - (tier * 0.1f);

        var matrix = new ColorMatrix(new float[][]
        {
            [agingScale, 0, 0, 0, 0],
            [0, agingScale, 0, 0, 0],
            [0, 0, agingScale, 0, 0],
            [0, 0, 0, alphaMul, 0],
            [grayOffset, grayOffset, grayOffset, 0, 1],
        });

        using var attrs = new ImageAttributes();
        attrs.SetColorMatrix(matrix);

        var aged = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(aged);
        g.DrawImage(source, new Rectangle(0, 0, size, size), 0, 0, size, size, GraphicsUnit.Pixel, attrs);
        return aged;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var bmp in _cache.Values) bmp.Dispose();
            _cache.Clear();
        }
        base.Dispose(disposing);
    }
}
