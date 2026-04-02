using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Imrdy.Windows.Icons;

/// <summary>
/// Renders DPI-aware colored circle icons for the system tray.
/// Uses SystemInformation.SmallIconSize for correct sizing.
/// Safe icon creation pattern: GetHicon → FromHandle.Clone → DestroyIcon.
/// </summary>
internal static class CircleIconRenderer
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>
    /// Creates a circle icon with the given RGB color and aging factor.
    /// </summary>
    /// <param name="r">Red component (0-255).</param>
    /// <param name="g">Green component (0-255).</param>
    /// <param name="b">Blue component (0-255).</param>
    /// <param name="agingFactor">Brightness factor (1.0 = full, 0.4 = darkest).</param>
    public static Icon CreateCircleIcon(byte r, byte g, byte b, double agingFactor = 1.0)
    {
        var size = SystemInformation.SmallIconSize;
        var agedR = (byte)(r * agingFactor);
        var agedG = (byte)(g * agingFactor);
        var agedB = (byte)(b * agingFactor);

        using var bitmap = new Bitmap(size.Width, size.Height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        using var brush = new SolidBrush(Color.FromArgb(agedR, agedG, agedB));
        // Draw circle with 1px padding to avoid clipping
        graphics.FillEllipse(brush, 1, 1, size.Width - 2, size.Height - 2);

        var hIcon = bitmap.GetHicon();
        try
        {
            // Clone creates a managed copy independent of the unmanaged handle
            var icon = (Icon)Icon.FromHandle(hIcon).Clone();
            return icon;
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    /// <summary>
    /// Calculates the aging factor based on time since last interaction.
    /// 5 tiers: 0-1m (1.0), 1-3m (0.85), 3-7m (0.70), 7-15m (0.55), 15m+ (0.40).
    /// </summary>
    public static double GetAgingFactor(TimeSpan timeSinceLastSeen)
    {
        var minutes = timeSinceLastSeen.TotalMinutes;
        return minutes switch
        {
            < 1 => 1.0,
            < 3 => 0.85,
            < 7 => 0.70,
            < 15 => 0.55,
            _ => 0.40,
        };
    }
}
