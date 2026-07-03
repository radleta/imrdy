using System.Drawing;

namespace Imrdy.Core.Overlay;

/// <summary>
/// Pure, WinForms-free placement geometry for the overlay panel: offset resolution,
/// anchor&lt;-&gt;offset conversion, edge/corner snap, and working-area clamp.
/// Mirrors the <see cref="Imrdy.Core.Display.DisplayItemCollection.TryGetItemAtClientPoint"/>
/// precedent — <c>System.Drawing.Primitives</c> types only, no <c>System.Windows.Forms</c>.
/// The Windows-side <c>OverlayPanel</c> is a thin wrapper that supplies
/// <c>Screen.WorkingArea</c>/panel size and never pre-resolves a null offset itself.
/// </summary>
public static class OverlayPlacement
{
    private const int Margin = 16;
    private const int BottomTaskbarReserve = 8;

    /// <summary>
    /// Resolves the panel's top-left origin. Owns the full null-resolution chain for
    /// <paramref name="offsetX"/>/<paramref name="offsetY"/> — callers pass the raw
    /// nullable config fields straight through; this is the single tested place the
    /// offset→anchor→default fallback is decided.
    /// </summary>
    /// <param name="offsetX">Monitor-relative logical-px X offset, or <c>null</c> to fall back to <paramref name="positionAnchor"/>.</param>
    /// <param name="offsetY">Monitor-relative logical-px Y offset, or <c>null</c> to fall back to <paramref name="positionAnchor"/>.</param>
    /// <param name="positionAnchor">Legacy anchor string (e.g. "bottom-right"); parsed via <see cref="OverlayAnchor.Parse"/>. Used only when either offset is <c>null</c>.</param>
    /// <param name="workingArea">Target monitor's working area (logical px).</param>
    /// <param name="panelSize">Panel size (logical px).</param>
    /// <returns>
    /// When both offsets are present: the offset interpreted as monitor-relative and
    /// clamped inside <paramref name="workingArea"/> via <see cref="ClampToWorkingArea"/>.
    /// When either offset is <c>null</c>: the anchored origin for <paramref name="positionAnchor"/>
    /// (or the default Right/Bottom anchor when the string is null, blank, or unrecognized).
    /// </returns>
    public static Point ResolveOrigin(
        int? offsetX,
        int? offsetY,
        string? positionAnchor,
        Rectangle workingArea,
        Size panelSize)
    {
        if (offsetX.HasValue && offsetY.HasValue)
        {
            var offsetOrigin = new Point(workingArea.Left + offsetX.Value, workingArea.Top + offsetY.Value);
            return ClampToWorkingArea(offsetOrigin, panelSize, workingArea);
        }

        var anchor = OverlayAnchor.Parse(positionAnchor);
        return ResolveAnchorOrigin(anchor, workingArea, panelSize);
    }

    /// <summary>
    /// Resolves an anchor string to its working-area-relative offset, for the menu's
    /// position presets (D7) to write alongside the legacy <c>position</c> field.
    /// </summary>
    /// <param name="anchorString">Anchor string; parsed via <see cref="OverlayAnchor.Parse"/> (null/blank/garbage → default Right/Bottom).</param>
    /// <param name="workingArea">Target monitor's working area (logical px).</param>
    /// <param name="panelSize">Panel size (logical px).</param>
    /// <returns>The offset, relative to <paramref name="workingArea"/>'s top-left, that lands the panel at that anchor.</returns>
    public static (int X, int Y) AnchorToOffset(string anchorString, Rectangle workingArea, Size panelSize)
    {
        var anchor = OverlayAnchor.Parse(anchorString);
        var origin = ResolveAnchorOrigin(anchor, workingArea, panelSize);
        return (origin.X - workingArea.Left, origin.Y - workingArea.Top);
    }

    /// <summary>
    /// Snaps <paramref name="origin"/> flush to a working-area edge when the panel edge on
    /// that axis is within <paramref name="thresholdLogicalPx"/>. Each axis is evaluated
    /// independently, so a corner snaps both when both axes qualify.
    /// </summary>
    /// <param name="origin">Candidate panel origin (logical px).</param>
    /// <param name="panelSize">Panel size (logical px).</param>
    /// <param name="workingArea">Target monitor's working area (logical px).</param>
    /// <param name="thresholdLogicalPx">Snap distance threshold in logical px; the panel edge snaps when its distance to the working-area edge is strictly less than this value.</param>
    /// <returns><paramref name="origin"/> with each axis snapped flush where within threshold; unchanged axes are returned as-is.</returns>
    public static Point ComputeEdgeSnap(Point origin, Size panelSize, Rectangle workingArea, int thresholdLogicalPx = 24)
    {
        int x = origin.X;
        int y = origin.Y;

        var distLeft = Math.Abs(origin.X - workingArea.Left);
        var distRight = Math.Abs(origin.X + panelSize.Width - workingArea.Right);
        if (distLeft < thresholdLogicalPx)
            x = workingArea.Left;
        else if (distRight < thresholdLogicalPx)
            x = workingArea.Right - panelSize.Width;

        var distTop = Math.Abs(origin.Y - workingArea.Top);
        var distBottom = Math.Abs(origin.Y + panelSize.Height - workingArea.Bottom);
        if (distTop < thresholdLogicalPx)
            y = workingArea.Top;
        else if (distBottom < thresholdLogicalPx)
            y = workingArea.Bottom - panelSize.Height;

        return new Point(x, y);
    }

    /// <summary>
    /// Clamps <paramref name="origin"/> so the whole panel stays inside
    /// <paramref name="workingArea"/> — the panel can never be dragged fully off-screen (D9).
    /// </summary>
    /// <param name="origin">Candidate panel origin (logical px).</param>
    /// <param name="panelSize">Panel size (logical px).</param>
    /// <param name="workingArea">Target monitor's working area (logical px).</param>
    /// <returns><paramref name="origin"/> unchanged when already fully inside; otherwise clamped to the nearest in-bounds position.</returns>
    public static Point ClampToWorkingArea(Point origin, Size panelSize, Rectangle workingArea)
    {
        // Math.Max guards a panel wider/taller than the working area: Math.Clamp throws if
        // min > max, so the upper bound must never fall below the lower bound.
        var maxX = Math.Max(workingArea.Left, workingArea.Right - panelSize.Width);
        var maxY = Math.Max(workingArea.Top, workingArea.Bottom - panelSize.Height);

        var x = Math.Clamp(origin.X, workingArea.Left, maxX);
        var y = Math.Clamp(origin.Y, workingArea.Top, maxY);

        return new Point(x, y);
    }

    /// <summary>
    /// Shared anchor→origin math for both <see cref="ResolveOrigin"/>'s anchor fallback
    /// branch and <see cref="AnchorToOffset"/>. Mirrors the existing
    /// <c>OverlayPanel.CalculatePosition</c> formula (margin + bottom taskbar reserve).
    /// </summary>
    private static Point ResolveAnchorOrigin(OverlayAnchor anchor, Rectangle workingArea, Size panelSize)
    {
        var x = anchor.Horizontal switch
        {
            HorizontalAnchor.Left => workingArea.Left + Margin,
            HorizontalAnchor.Center => workingArea.Left + (workingArea.Width - panelSize.Width) / 2,
            _ => workingArea.Right - panelSize.Width - Margin, // Right
        };
        var y = anchor.Vertical switch
        {
            VerticalAnchor.Top => workingArea.Top + Margin,
            _ => workingArea.Bottom - panelSize.Height - BottomTaskbarReserve, // Bottom
        };
        return new Point(x, y);
    }
}
