namespace Imrdy.Core.Diagnostics;

/// <summary>
/// A single node in the flattened control tree.
/// Child indexes reference positions in the parent <see cref="InspectResult.Tree"/> list to avoid cycles.
/// </summary>
/// <param name="Details">
/// Optional supplementary string→string metadata. For <c>TableLayoutPanel</c> nodes this map
/// carries per-row computed heights as <c>"row[0]"</c>, <c>"row[1]"</c>, etc.
/// Never null — always an empty dictionary when unused.
/// </param>
public record LayoutNode(
    string Type,
    string Name,
    string Text,
    int BoundsX,
    int BoundsY,
    int BoundsWidth,
    int BoundsHeight,
    string ForeColor,
    string BackColor,
    string FontName,
    float FontSize,
    string FontStyle,
    string Anchor,
    string Dock,
    bool Visible,
    int PaddingLeft,
    int PaddingTop,
    int PaddingRight,
    int PaddingBottom,
    int MarginLeft,
    int MarginTop,
    int MarginRight,
    int MarginBottom,
    int[] ChildIndexes,
    IReadOnlyDictionary<string, string> Details);
