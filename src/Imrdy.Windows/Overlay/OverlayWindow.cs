using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Imrdy.Core;
using Imrdy.Core.Graphics;
using Imrdy.Core.Status;
using Imrdy.Windows.Desktop;
using Microsoft.Extensions.Logging;
using Svg;

namespace Imrdy.Windows.Overlay;

internal sealed class OverlayWindow : Form
{
    private readonly OverlayConfig _config;
    private readonly ILogger<OverlayWindow> _logger;
    private readonly System.Windows.Forms.Timer _topmostTimer;
    private readonly GraphicsPackLoader _graphicsPackLoader;
    private readonly string _iconStyle;
    private readonly Dictionary<(string status, int tier), Bitmap> _cache = new();

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
        _iconStyle = ConfigReader.Read().Tray.IconStyle;

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

        PreRenderAll();
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
            var key = (_cache.ContainsKey((s.Status, s.AgingTier))
                ? (s.Status, s.AgingTier)
                : _cache.ContainsKey(("unknown", s.AgingTier))
                    ? ("unknown", s.AgingTier)
                    : ("idle", 0)); // ultimate fallback
            if (_cache.TryGetValue(key, out var bmp))
            {
                var x = i * (_config.Size + _config.Spacing);
                g.DrawImageUnscaled(bmp, x, 0);
            }
        }

        var position = CalculatePosition(totalWidth);
        PInvokeOverlay.SetBitmap(Handle, composite, position);
        Visible = true;
    }

    private Point CalculatePosition(int contentWidth)
    {
        var wa = Screen.PrimaryScreen!.WorkingArea;
        var y = wa.Bottom - _config.Size - 16;
        var x = _config.Position == "bottom-left"
            ? wa.Left + 16
            : wa.Right - contentWidth - 16;
        return new Point(x, y);
    }

    private void PreRenderAll()
    {
        var size = _config.Size;

        if (_iconStyle.StartsWith("pack:", StringComparison.OrdinalIgnoreCase))
        {
            var packName = _iconStyle["pack:".Length..];
            var packsRoot = Path.GetFullPath(ImrdyPaths.GraphicsPacksDir);
            var packDir = Path.GetFullPath(Path.Combine(packsRoot, packName));
            if (!packDir.StartsWith(packsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("OverlayWindow: invalid pack name '{Pack}' — path traversal detected, falling back to dots.", packName);
                PreRenderDots(size);
                return;
            }
            var packJsonPath = Path.Combine(packDir, "pack.json");
            var pack = _graphicsPackLoader.LoadPack(packDir, packJsonPath);

            if (pack is not null)
            {
                try
                {
                    PreRenderFromPack(pack, size);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "OverlayWindow: failed to pre-render pack '{Pack}', falling back to dots.", packName);
                    foreach (var bmp in _cache.Values) bmp.Dispose();
                    _cache.Clear();
                }
            }
            else
            {
                _logger.LogWarning("OverlayWindow: pack '{Pack}' could not be loaded, falling back to dots.", packName);
            }
        }

        PreRenderDots(size);
    }

    private void PreRenderFromPack(GraphicsPackLoader.LoadedGraphicsPack pack, int size)
    {
        foreach (var (state, filePath) in pack.StateFilePaths)
        {
            // SvgDocument does not implement IDisposable in Svg.NET 3.4.7 — do not wrap in using
            var doc = SvgDocument.Open(filePath);
            using var baseBitmap = EnsurePArgb(doc.Draw(size, size), size);

            for (var tier = 0; tier <= 4; tier++)
            {
                var bmp = tier == 0
                    ? ClonePArgb(baseBitmap, size)
                    : ApplyAgingColorMatrix(baseBitmap, size, tier);
                _cache[(state, tier)] = bmp;
            }
        }
    }

    private static void PreRenderDots(Dictionary<(string status, int tier), Bitmap> cache, int size)
    {
        foreach (var status in StatusMap.KnownBaseStatuses)
        {
            var (r, g, b) = StatusMap.ResolveColor(status);

            for (var tier = 0; tier <= 4; tier++)
            {
                var factor = StatusMap.GetAgingFactorFromTier(tier);
                cache[(status, tier)] = CreateCircleBitmap(r, g, b, factor, size);
            }
        }
    }

    private void PreRenderDots(int size) => PreRenderDots(_cache, size);

    private static Bitmap CreateCircleBitmap(byte r, byte g, byte b, double agingFactor, int size)
    {
        var agedR = (byte)(r * agingFactor);
        var agedG = (byte)(g * agingFactor);
        var agedB = (byte)(b * agingFactor);

        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        using var brush = new SolidBrush(Color.FromArgb(agedR, agedG, agedB));
        graphics.FillEllipse(brush, 1, 1, size - 2, size - 2);

        return bitmap;
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
