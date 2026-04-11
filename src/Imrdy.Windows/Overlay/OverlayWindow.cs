using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Imrdy.Core;
using Imrdy.Core.Graphics;
using Imrdy.Core.Icons;
using Imrdy.Core.Status;
using Imrdy.Windows.Desktop;
using Imrdy.Windows.Icons;
using Microsoft.Extensions.Logging;
using Svg;

namespace Imrdy.Windows.Overlay;

internal sealed class OverlayWindow : Form
{
    private readonly OverlayConfig _config;
    private readonly ILogger<OverlayWindow> _logger;
    private readonly System.Windows.Forms.Timer _topmostTimer;
    private readonly GraphicsPackLoader _graphicsPackLoader;
    private readonly Dictionary<(string style, string status, int tier), Bitmap> _cache = new();

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= PInvokeOverlay.WS_EX_LAYERED;
            cp.ExStyle |= PInvokeOverlay.WS_EX_NOACTIVATE;
            cp.ExStyle |= PInvokeOverlay.WS_EX_TOOLWINDOW;
            cp.ExStyle |= PInvokeOverlay.WS_EX_TRANSPARENT; // Full click-through per D13
            return cp;
        }
    }

    public OverlayWindow(OverlayConfig config, ILoggerFactory loggerFactory, GraphicsPackLoader graphicsPackLoader)
    {
        _config = config;
        _logger = loggerFactory.CreateLogger<OverlayWindow>();
        _graphicsPackLoader = graphicsPackLoader;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;

        _topmostTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _topmostTimer.Tick += (_, _) =>
        {
            if (IsHandleCreated)
                PInvokeOverlay.ReapplyTopMost(Handle);
        };

        Load += (_, _) => _topmostTimer.Start();
    }

    private const int MaxVisibleSessions = 50;

    public void UpdateSessions(IReadOnlyList<OverlaySessionInfo> sessions)
    {
        if (sessions.Count == 0) { Visible = false; return; }

        var visibleCount = Math.Min(sessions.Count, MaxVisibleSessions);
        var totalWidth = visibleCount * _config.Size + (visibleCount - 1) * _config.Spacing;
        using var composite = new Bitmap(totalWidth, _config.Size, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(composite);
        g.Clear(Color.Transparent);

        for (int i = 0; i < visibleCount; i++)
        {
            var s = sessions[i];
            var bmp = GetOrCreateBitmap(s.IconStyle, s.Status, s.AgingTier);
            var x = i * (_config.Size + _config.Spacing);
            g.DrawImageUnscaled(bmp, x, 0);
        }

        var position = CalculatePosition(totalWidth);
        PInvokeOverlay.SetBitmap(Handle, composite, position);
        Visible = true;
    }

    /// <summary>
    /// Clears and disposes all cached bitmaps. Does not eagerly re-render;
    /// the cache refills lazily on the next UpdateSessions call.
    /// </summary>
    public void InvalidateStyleCache()
    {
        foreach (var bmp in _cache.Values) bmp.Dispose();
        _cache.Clear();
    }

    private Bitmap GetOrCreateBitmap(string style, string status, int tier)
    {
        var key = (style, status, tier);
        if (_cache.TryGetValue(key, out var cached))
            return cached;

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
            _logger.LogWarning(ex, "OverlayWindow: failed to render bitmap for style='{Style}' status='{Status}' tier={Tier}, falling back to circle.", style, status, tier);
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

        // Unknown style — fall through to circle fallback
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
        {
            throw new InvalidOperationException($"Invalid pack name '{packName}' — path traversal detected.");
        }

        var packJsonPath = Path.Combine(packDir, "pack.json");
        var pack = _graphicsPackLoader.LoadPack(packDir, packJsonPath)
            ?? throw new InvalidOperationException($"Pack '{packName}' could not be loaded.");

        if (!pack.StateFilePaths.TryGetValue(status, out var filePath))
        {
            // Status not in pack — try "unknown" then "idle"
            if (!pack.StateFilePaths.TryGetValue("unknown", out filePath) &&
                !pack.StateFilePaths.TryGetValue("idle", out filePath))
            {
                throw new InvalidOperationException($"Pack '{packName}' has no entry for status '{status}'.");
            }
        }

        // SvgDocument does not implement IDisposable in Svg.NET 3.4.7 — do not wrap in using
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
            _logger.LogError(ex, "OverlayWindow: even circle fallback failed for status='{Status}' tier={Tier}.", status, tier);
            // Last resort: return transparent bitmap so rendering doesn't crash
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

    /// <summary>
    /// Ensures source bitmap is Format32bppPArgb. Converts if needed, disposing the source.
    /// </summary>
    private static Bitmap EnsurePArgb(Bitmap source, int size)
    {
        if (source.PixelFormat == PixelFormat.Format32bppPArgb)
            return source;

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

    /// <summary>
    /// Applies aging desaturation + dim toward gray via ColorMatrix.
    /// Mirrors PackIconRenderer.ApplyAgingColorMatrix but outputs Format32bppPArgb per D18.
    /// </summary>
    private static Bitmap ApplyAgingColorMatrix(Bitmap source, int size, int tier)
    {
        // agingScale: how much of the original color to keep (vs. blend to 0.5 gray)
        // tier 1 → 0.75, tier 2 → 0.5, tier 3 → 0.25, tier 4 → 0.0 (fully gray)
        var agingScale = 1.0f - (tier / 4.0f);
        var grayOffset = (1.0f - agingScale) * 0.5f;

        // Alpha dim: tier 1 → 0.9, tier 2 → 0.8, tier 3 → 0.7, tier 4 → 0.6
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
            _topmostTimer?.Stop();
            _topmostTimer?.Dispose();
            foreach (var bmp in _cache.Values) bmp.Dispose();
            _cache.Clear();
        }
        base.Dispose(disposing);
    }
}
