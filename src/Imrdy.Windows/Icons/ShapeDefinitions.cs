using System.Drawing;

namespace Imrdy.Windows.Icons;

/// <summary>
/// GDI+ draw delegates for each built-in tray icon shape.
/// Each delegate receives a Graphics context, a padded draw rect, and a pre-constructed brush.
/// The delegate fills the shape within the rect — it must not create or dispose the brush.
/// </summary>
public static class ShapeDefinitions
{
    /// <summary>Filled circle (ellipse inscribed in rect).</summary>
    public static readonly Action<Graphics, RectangleF, Brush> Circle =
        (g, rect, brush) => g.FillEllipse(brush, rect);

    /// <summary>Filled square.</summary>
    public static readonly Action<Graphics, RectangleF, Brush> Square =
        (g, rect, brush) => g.FillRectangle(brush, rect);

    /// <summary>Filled upward-pointing equilateral triangle inscribed in rect.</summary>
    public static readonly Action<Graphics, RectangleF, Brush> Triangle = (g, rect, brush) =>
    {
        var points = new PointF[]
        {
            new(rect.Left + rect.Width / 2f, rect.Top),
            new(rect.Right, rect.Bottom),
            new(rect.Left, rect.Bottom),
        };
        g.FillPolygon(brush, points);
    };

    /// <summary>Filled diamond (rotated square) inscribed in rect.</summary>
    public static readonly Action<Graphics, RectangleF, Brush> Diamond = (g, rect, brush) =>
    {
        var cx = rect.Left + rect.Width / 2f;
        var cy = rect.Top + rect.Height / 2f;
        var points = new PointF[]
        {
            new(cx, rect.Top),
            new(rect.Right, cy),
            new(cx, rect.Bottom),
            new(rect.Left, cy),
        };
        g.FillPolygon(brush, points);
    };

    /// <summary>Filled regular hexagon inscribed in rect.</summary>
    public static readonly Action<Graphics, RectangleF, Brush> Hexagon = (g, rect, brush) =>
    {
        var cx = rect.Left + rect.Width / 2f;
        var cy = rect.Top + rect.Height / 2f;
        var rx = rect.Width / 2f;
        var ry = rect.Height / 2f;
        var points = new PointF[6];
        for (var i = 0; i < 6; i++)
        {
            // Start at top (270°), step 60° clockwise
            var angle = (Math.PI / 180.0) * (270 + i * 60);
            points[i] = new PointF(
                cx + rx * (float)Math.Cos(angle),
                cy + ry * (float)Math.Sin(angle));
        }
        g.FillPolygon(brush, points);
    };

    /// <summary>
    /// Filled plus sign (~40% arm width relative to rect size).
    /// Composed of two overlapping rectangles (horizontal bar + vertical bar).
    /// </summary>
    public static readonly Action<Graphics, RectangleF, Brush> Plus = (g, rect, brush) =>
    {
        var armWidth = rect.Width * 0.4f;
        var hBarX = rect.Left;
        var hBarY = rect.Top + (rect.Height - armWidth) / 2f;
        var hBar = new RectangleF(hBarX, hBarY, rect.Width, armWidth);

        var vBarX = rect.Left + (rect.Width - armWidth) / 2f;
        var vBarY = rect.Top;
        var vBar = new RectangleF(vBarX, vBarY, armWidth, rect.Height);

        g.FillRectangle(brush, hBar);
        g.FillRectangle(brush, vBar);
    };
}
