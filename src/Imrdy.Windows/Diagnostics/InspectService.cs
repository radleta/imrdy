using System.Collections.Immutable;
using System.Drawing;
using System.Windows.Forms;
using Imrdy.Core.Diagnostics;

namespace Imrdy.Windows.Diagnostics;

/// <summary>
/// Stateless walker that traverses a <see cref="Form"/> control tree and returns
/// a flat <see cref="LayoutNode"/> list plus <see cref="FormGeometry"/>.
///
/// Call <see cref="Walk"/> after the form has been shown and laid out (Show +
/// DoEvents + PerformLayout) so child bounds reflect their final AutoSize values.
/// Each call is independent — no caches or retained references (D7).
/// </summary>
internal static class InspectService
{
    private const int MaxTextLength = 200;

    /// <summary>
    /// Walks the control tree rooted at <paramref name="form"/> via BFS and returns
    /// a flat node list plus the form's geometry snapshot.
    /// </summary>
    /// <param name="form">The form to inspect. Must be shown and laid out.</param>
    /// <param name="regionRadius">
    /// The rounded-rect clip radius applied to the form. Pass 0 if no region is set.
    /// Carried through to <see cref="FormGeometry.RegionRadius"/> for the analyzer.
    /// </param>
    public static (FormGeometry Geom, IReadOnlyList<LayoutNode> Tree) Walk(Form form, int regionRadius)
    {
        var geom = new FormGeometry(
            form.Bounds.X,
            form.Bounds.Y,
            form.Bounds.Width,
            form.Bounds.Height,
            form.ClientSize.Width,
            form.ClientSize.Height,
            regionRadius);

        var nodes = new List<LayoutNode>();

        // BFS queue carries (control, parentFormOffset) where the offset is the
        // cumulative Left/Top displacement from the form's client origin.
        var queue = new Queue<(Control Control, Point Offset, int ParentIndex)>();

        // Form itself is index 0; its client origin is (0,0) relative to itself.
        queue.Enqueue((form, Point.Empty, -1));

        // childIndexLists[i] accumulates the child node indexes for node i.
        var childIndexLists = new List<List<int>>();

        while (queue.Count > 0)
        {
            var (ctrl, offset, parentIdx) = queue.Dequeue();
            var myIndex = nodes.Count;

            // Tell parent about this child
            if (parentIdx >= 0)
                childIndexLists[parentIdx].Add(myIndex);

            // Reserve a slot for our own children list
            childIndexLists.Add(new List<int>());

            // Compute bounds relative to form client area.
            // For the form itself, bounds are its screen Bounds (we record Geom separately).
            // For child controls, project their Location up to form-client coordinates.
            Rectangle boundsInForm;
            if (ctrl is Form f)
            {
                // The form node records its own size; position is always (0,0) in form-coords.
                boundsInForm = new Rectangle(0, 0, f.ClientSize.Width, f.ClientSize.Height);
            }
            else
            {
                boundsInForm = new Rectangle(
                    offset.X + ctrl.Left,
                    offset.Y + ctrl.Top,
                    ctrl.Width,
                    ctrl.Height);
            }

            var details = BuildDetails(ctrl);
            var node = new LayoutNode(
                Type: ctrl.GetType().Name,
                Name: ctrl.Name ?? string.Empty,
                Text: TruncateText(ctrl.Text),
                BoundsX: boundsInForm.X,
                BoundsY: boundsInForm.Y,
                BoundsWidth: boundsInForm.Width,
                BoundsHeight: boundsInForm.Height,
                ForeColor: EncodeColor(ctrl.ForeColor),
                BackColor: EncodeColor(ctrl.BackColor),
                FontName: ctrl.Font?.Name ?? string.Empty,
                FontSize: ctrl.Font?.SizeInPoints ?? 0f,
                FontStyle: ctrl.Font?.Style.ToString() ?? string.Empty,
                Anchor: ctrl.Anchor.ToString(),
                Dock: ctrl.Dock.ToString(),
                Visible: ctrl.Visible,
                PaddingLeft: ctrl.Padding.Left,
                PaddingTop: ctrl.Padding.Top,
                PaddingRight: ctrl.Padding.Right,
                PaddingBottom: ctrl.Padding.Bottom,
                MarginLeft: ctrl.Margin.Left,
                MarginTop: ctrl.Margin.Top,
                MarginRight: ctrl.Margin.Right,
                MarginBottom: ctrl.Margin.Bottom,
                ChildIndexes: Array.Empty<int>(), // placeholder — filled in after BFS
                Details: details);

            nodes.Add(node);

            // Compute the offset to pass to each direct child.
            // For the form, children are positioned relative to the client area, so offset stays (0,0).
            // For other controls, add this control's position to the running offset.
            Point childOffset;
            if (ctrl is Form)
                childOffset = Point.Empty;
            else
                childOffset = new Point(offset.X + ctrl.Left, offset.Y + ctrl.Top);

            foreach (Control child in ctrl.Controls)
                queue.Enqueue((child, childOffset, myIndex));
        }

        // Patch child indexes now that all nodes are assigned positions.
        var result = new List<LayoutNode>(nodes.Count);
        for (var i = 0; i < nodes.Count; i++)
        {
            var children = childIndexLists[i].Count > 0
                ? childIndexLists[i].ToArray()
                : Array.Empty<int>();

            result.Add(nodes[i] with { ChildIndexes = children });
        }

        return (geom, result.AsReadOnly());
    }

    // ---- Helpers ----

    private static IReadOnlyDictionary<string, string> BuildDetails(Control ctrl)
    {
        if (ctrl is not TableLayoutPanel tlp)
            return ImmutableDictionary<string, string>.Empty;

        int[] rowHeights;
        try
        {
            rowHeights = tlp.GetRowHeights();
        }
        catch
        {
            return ImmutableDictionary<string, string>.Empty;
        }

        if (rowHeights.Length == 0)
            return ImmutableDictionary<string, string>.Empty;

        var builder = ImmutableDictionary.CreateBuilder<string, string>();
        for (var i = 0; i < rowHeights.Length; i++)
            builder[$"row[{i}]"] = rowHeights[i].ToString();

        return builder.ToImmutable();
    }

    /// <summary>
    /// Encodes a <see cref="Color"/> as <c>"#RRGGBB"</c>.
    /// <see cref="Color.Empty"/> and <see cref="Color.Transparent"/> map to <c>"transparent"</c>.
    /// Alpha channel is dropped — callers should not rely on alpha in this field.
    /// </summary>
    private static string EncodeColor(Color c)
    {
        if (c.IsEmpty || c == Color.Transparent)
            return "transparent";

        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    private static string TruncateText(string? text)
    {
        if (text is null)
            return string.Empty;

        if (text.Length <= MaxTextLength)
            return text;

        return text[..197] + "...";
    }
}
