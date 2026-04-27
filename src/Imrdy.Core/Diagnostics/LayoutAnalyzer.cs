using System.Collections.Immutable;

namespace Imrdy.Core.Diagnostics;

/// <summary>
/// Pure, stateless analyzer over a walked <see cref="LayoutNode"/> tree.
/// Produces <see cref="DiagnosticFinding"/> instances across four categories.
/// No filesystem, network, or threading — safe to call on any thread.
/// </summary>
public static class LayoutAnalyzer
{
    // ---- Thresholds ----

    /// <summary>Side length of the corner-clip box in pixels. Equals the form's RegionRadius.</summary>
    // Used as-is from FormGeometry.RegionRadius; defined here for documentation continuity.

    /// <summary>Minimum clipped pixel area (intersection area) that triggers a regionClipRisk finding.</summary>
    private const int MinClipArea = 1;

    /// <summary>Minimum overlap area in pixels² that triggers a siblingOverlap finding.</summary>
    private const int MinOverlapArea = 1;

    /// <summary>
    /// Advisory proximity floor in pixels. A control whose bounds come within this many pixels
    /// of any form edge is reported as <c>edgeProximity</c> (info severity).
    /// </summary>
    private const int EdgeProximityFloor = 4;

    // ---- Allow-list for known-OK overlapping sibling pairs ----

    /// <summary>
    /// Ordered pairs (A, B) where A.Type+A.Name and B.Type+B.Name are both allowed to overlap.
    /// Stored as sorted tuples so insertion order doesn't matter (symmetric lookup via <see cref="Normalized"/>).
    /// </summary>
    private static readonly HashSet<(string, string, string, string)> _overlapAllowPairs =
        new()
        {
            // accent bar overlaps the header panel — intentional design (absolute-positioned stripe)
            Normalized("Panel", "accentBar", "Panel", "headerPanel"),
        };

    // ---- Public API ----

    /// <summary>
    /// Analyzes the supplied control tree and returns all detected layout findings.
    /// Findings are appended in detector order: regionClipRisk, siblingOverlap, edgeProximity, collapsedRow.
    /// </summary>
    public static IReadOnlyList<DiagnosticFinding> Analyze(
        FormGeometry form,
        IReadOnlyList<LayoutNode> tree)
    {
        var findings = new List<DiagnosticFinding>();

        // Build the parent-index map once; all detectors share it via BuildControlPath.
        var parentOf = BuildParentMap(tree);

        CheckClipRisk(form, tree, parentOf, findings);
        CheckSiblingOverlap(tree, parentOf, findings);
        CheckEdgeProximity(form, tree, parentOf, findings);
        CheckCollapsedRows(tree, parentOf, findings);

        return findings.AsReadOnly();
    }

    // ---- Detectors ----

    private static void CheckClipRisk(
        FormGeometry form,
        IReadOnlyList<LayoutNode> tree,
        Dictionary<int, int> parentOf,
        List<DiagnosticFinding> findings)
    {
        int r = form.RegionRadius;
        if (r <= 0)
            return;

        int fw = form.FormWidth;
        int fh = form.FormHeight;

        // Four corner boxes: (left, top, width, height) in form-client coordinates.
        var corners = new (int Left, int Top, int Right, int Bottom, string Name)[]
        {
            (0,      0,      r,  r,  "top-left"),
            (fw - r, 0,      fw, r,  "top-right"),
            (0,      fh - r, r,  fh, "bottom-left"),
            (fw - r, fh - r, fw, fh, "bottom-right"),
        };

        // tree[0] is always the form root — skip it (its bounds cover the entire form)
        for (int nodeIdx = 1; nodeIdx < tree.Count; nodeIdx++)
        {
            var node = tree[nodeIdx];

            if (!node.Visible)
                continue;

            if (node.BoundsWidth <= 0 || node.BoundsHeight <= 0)
                continue;

            int nl = node.BoundsX;
            int nt = node.BoundsY;
            int nr = node.BoundsX + node.BoundsWidth;
            int nb = node.BoundsY + node.BoundsHeight;

            foreach (var (cl, ct, cr, cb, cornerName) in corners)
            {
                // Intersection of node bounds with corner box
                int il = Math.Max(nl, cl);
                int it = Math.Max(nt, ct);
                int ir = Math.Min(nr, cr);
                int ib = Math.Min(nb, cb);

                if (ir <= il || ib <= it)
                    continue; // no intersection

                int area = (ir - il) * (ib - it);
                if (area < MinClipArea)
                    continue;

                bool hasText = !string.IsNullOrEmpty(node.Text);
                string severity = hasText ? "error" : "warning";
                string path = BuildControlPath(tree, parentOf, node);

                findings.Add(new DiagnosticFinding(
                    Kind: "regionClipRisk",
                    Severity: severity,
                    ControlPath: path,
                    Message: "control bounds intersect rounded-corner clip",
                    Details: ImmutableDictionary.CreateRange(new[]
                    {
                        new KeyValuePair<string, string>("corner", cornerName),
                        new KeyValuePair<string, string>("clippedPixels", area.ToString()),
                    })));
            }
        }
    }

    private static void CheckSiblingOverlap(
        IReadOnlyList<LayoutNode> tree,
        Dictionary<int, int> parentOf,
        List<DiagnosticFinding> findings)
    {
        foreach (var parent in tree)
        {
            var childIndexes = parent.ChildIndexes;
            if (childIndexes.Length < 2)
                continue;

            // Collect only visible children with positive area
            var visible = new List<(int Index, LayoutNode Node)>();
            foreach (int ci in childIndexes)
            {
                if (ci < 0 || ci >= tree.Count)
                    continue;
                var child = tree[ci];
                if (child.Visible && child.BoundsWidth > 0 && child.BoundsHeight > 0)
                    visible.Add((ci, child));
            }

            for (int i = 0; i < visible.Count; i++)
            {
                for (int j = i + 1; j < visible.Count; j++)
                {
                    var (_, a) = visible[i];
                    var (_, b) = visible[j];

                    if (IsAllowedOverlap(a, b))
                        continue;

                    int il = Math.Max(a.BoundsX, b.BoundsX);
                    int it = Math.Max(a.BoundsY, b.BoundsY);
                    int ir = Math.Min(a.BoundsX + a.BoundsWidth, b.BoundsX + b.BoundsWidth);
                    int ib = Math.Min(a.BoundsY + a.BoundsHeight, b.BoundsY + b.BoundsHeight);

                    if (ir <= il || ib <= it)
                        continue;

                    int area = (ir - il) * (ib - it);
                    if (area < MinOverlapArea)
                        continue;

                    string parentPath = BuildControlPath(tree, parentOf, parent);
                    findings.Add(new DiagnosticFinding(
                        Kind: "siblingOverlap",
                        Severity: "warning",
                        ControlPath: parentPath,
                        Message: $"sibling controls '{SegmentFor(a)}' and '{SegmentFor(b)}' overlap by {area}px²",
                        Details: ImmutableDictionary.CreateRange(new[]
                        {
                            new KeyValuePair<string, string>("controlA", SegmentFor(a)),
                            new KeyValuePair<string, string>("controlB", SegmentFor(b)),
                            new KeyValuePair<string, string>("overlapArea", area.ToString()),
                        })));
                }
            }
        }
    }

    private static void CheckEdgeProximity(
        FormGeometry form,
        IReadOnlyList<LayoutNode> tree,
        Dictionary<int, int> parentOf,
        List<DiagnosticFinding> findings)
    {
        int fw = form.FormWidth;
        int fh = form.FormHeight;

        // tree[0] is always the form root — skip it
        for (int nodeIdx = 1; nodeIdx < tree.Count; nodeIdx++)
        {
            var node = tree[nodeIdx];

            if (!node.Visible)
                continue;

            if (node.BoundsWidth <= 0 || node.BoundsHeight <= 0)
                continue;

            int nl = node.BoundsX;
            int nt = node.BoundsY;
            int nr = node.BoundsX + node.BoundsWidth;
            int nb = node.BoundsY + node.BoundsHeight;

            var edges = new List<string>();
            if (nl < EdgeProximityFloor) edges.Add("left");
            if (nt < EdgeProximityFloor) edges.Add("top");
            if (nr > fw - EdgeProximityFloor) edges.Add("right");
            if (nb > fh - EdgeProximityFloor) edges.Add("bottom");

            if (edges.Count == 0)
                continue;

            string path = BuildControlPath(tree, parentOf, node);
            findings.Add(new DiagnosticFinding(
                Kind: "edgeProximity",
                Severity: "info",
                ControlPath: path,
                Message: $"control is within {EdgeProximityFloor}px of form edge(s): {string.Join(", ", edges)}",
                Details: ImmutableDictionary.CreateRange(new[]
                {
                    new KeyValuePair<string, string>("edges", string.Join(",", edges)),
                })));
        }
    }

    private static void CheckCollapsedRows(
        IReadOnlyList<LayoutNode> tree,
        Dictionary<int, int> parentOf,
        List<DiagnosticFinding> findings)
    {
        foreach (var node in tree)
        {
            if (node.Type != "TableLayoutPanel")
                continue;

            foreach (var kvp in node.Details)
            {
                // Keys are "row[0]", "row[1]", etc.
                if (!kvp.Key.StartsWith("row[", StringComparison.Ordinal))
                    continue;

                if (kvp.Value != "0")
                    continue;

                // Parse row index from "row[N]"
                string inner = kvp.Key[4..^1]; // strip "row[" and "]"
                string path = BuildControlPath(tree, parentOf, node);
                findings.Add(new DiagnosticFinding(
                    Kind: "collapsedRow",
                    Severity: "info",
                    ControlPath: path,
                    Message: $"row {inner} collapsed",
                    Details: ImmutableDictionary.CreateRange(new[]
                    {
                        new KeyValuePair<string, string>("rowIndex", inner),
                    })));
            }
        }
    }

    // ---- ControlPath builder ----

    /// <summary>
    /// Builds a child→parent index map by inverting the child-index links (first parent wins).
    /// Pre-computed once per <see cref="Analyze"/> call so all detectors share one allocation.
    /// </summary>
    private static Dictionary<int, int> BuildParentMap(IReadOnlyList<LayoutNode> tree)
    {
        var parentOf = new Dictionary<int, int>(tree.Count);
        for (int i = 0; i < tree.Count; i++)
        {
            foreach (int ci in tree[i].ChildIndexes)
            {
                if (!parentOf.ContainsKey(ci))
                    parentOf[ci] = i;
            }
        }
        return parentOf;
    }

    /// <summary>
    /// Builds a slash-separated path from the form root to <paramref name="target"/>.
    /// Each segment is <c>Type</c> or <c>Type[Name]</c> (name omitted when empty).
    /// </summary>
    private static string BuildControlPath(
        IReadOnlyList<LayoutNode> tree,
        Dictionary<int, int> parentOf,
        LayoutNode target)
    {
        // Find the index of the target node
        int targetIndex = -1;
        for (int i = 0; i < tree.Count; i++)
        {
            if (ReferenceEquals(tree[i], target))
            {
                targetIndex = i;
                break;
            }
        }

        if (targetIndex < 0)
            return SegmentFor(target);

        // Walk up to root collecting segments
        var segments = new Stack<string>();
        int current = targetIndex;
        while (current >= 0)
        {
            segments.Push(SegmentFor(tree[current]));
            if (current == 0 && !parentOf.ContainsKey(0))
                break;
            current = parentOf.TryGetValue(current, out int parent) ? parent : -1;
        }

        return string.Join("/", segments);
    }

    private static string SegmentFor(LayoutNode node)
    {
        if (string.IsNullOrEmpty(node.Name))
            return node.Type;
        return $"{node.Type}[{node.Name}]";
    }

    // ---- Allow-list helpers ----

    private static bool IsAllowedOverlap(LayoutNode a, LayoutNode b)
    {
        var key = Normalized(a.Type, a.Name, b.Type, b.Name);
        return _overlapAllowPairs.Contains(key);
    }

    /// <summary>
    /// Returns a canonical (sorted) 4-tuple for the two type/name pairs so allow-list
    /// membership is symmetric regardless of which control is "a" vs "b".
    /// </summary>
    private static (string, string, string, string) Normalized(
        string typeA, string nameA, string typeB, string nameB)
    {
        string keyA = $"{typeA}\x00{nameA}";
        string keyB = $"{typeB}\x00{nameB}";
        if (string.CompareOrdinal(keyA, keyB) <= 0)
            return (typeA, nameA, typeB, nameB);
        return (typeB, nameB, typeA, nameA);
    }
}
