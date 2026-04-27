using System.Collections.Immutable;
using FluentAssertions;
using Imrdy.Core.Diagnostics;

namespace Imrdy.Core.Tests.Diagnostics;

/// <summary>
/// Unit tests for <see cref="LayoutAnalyzer"/>. All input data is synthetic — no WinForms
/// dependency. <see cref="LayoutNode"/> records are constructed directly.
/// </summary>
public class LayoutAnalyzerTests
{
    // ---- Shared form geometry (520×254, radius 14) ----

    private static readonly FormGeometry DefaultForm = new(
        FormX: 0, FormY: 0,
        FormWidth: 520, FormHeight: 254,
        ClientWidth: 520, ClientHeight: 254,
        RegionRadius: 14);

    // ---- Node factory helpers ----

    private static LayoutNode MakeNode(
        string type, string name, string text,
        int x, int y, int w, int h,
        bool visible = true,
        int[]? children = null,
        IReadOnlyDictionary<string, string>? details = null) =>
        new(
            Type: type, Name: name, Text: text,
            BoundsX: x, BoundsY: y, BoundsWidth: w, BoundsHeight: h,
            ForeColor: "#000000", BackColor: "#FFFFFF",
            FontName: "Arial", FontSize: 9f, FontStyle: "Regular",
            Anchor: "Top, Left", Dock: "None",
            Visible: visible,
            PaddingLeft: 0, PaddingTop: 0, PaddingRight: 0, PaddingBottom: 0,
            MarginLeft: 3, MarginTop: 3, MarginRight: 3, MarginBottom: 3,
            ChildIndexes: children ?? Array.Empty<int>(),
            Details: details ?? ImmutableDictionary<string, string>.Empty);

    // ---- regionClipRisk tests ----

    [Fact]
    public void ClipRisk_ControlInTopRightCorner_EmitsWarning()
    {
        // Bounds (490, 5, 20, 12): right edge=510 > 506, top=5 < 14 — intersects top-right corner box
        var form = DefaultForm;
        var root = MakeNode("DashboardForm", "DashboardForm", "", 0, 0, 520, 254, children: [1]);
        var ctrl = MakeNode("Label", "myLabel", "", 490, 5, 20, 12);
        var tree = new List<LayoutNode> { root, ctrl }.AsReadOnly();

        var findings = LayoutAnalyzer.Analyze(form, tree);

        findings.Should().ContainSingle(f => f.Kind == "regionClipRisk",
            "the control overlaps the top-right corner box");
        var finding = findings.First(f => f.Kind == "regionClipRisk");
        finding.Severity.Should().Be("warning", "control has no text");
        finding.Details["corner"].Should().Be("top-right");
    }

    [Fact]
    public void ClipRisk_ControlBelowCorner_NoFinding()
    {
        // Bounds (490, 16, 20, 12): top=16 > 14 — below the 14px corner box, no intersection
        var form = DefaultForm;
        var root = MakeNode("DashboardForm", "DashboardForm", "", 0, 0, 520, 254, children: [1]);
        var ctrl = MakeNode("Label", "myLabel", "", 490, 16, 20, 12);
        var tree = new List<LayoutNode> { root, ctrl }.AsReadOnly();

        var findings = LayoutAnalyzer.Analyze(form, tree);

        findings.Should().NotContain(f => f.Kind == "regionClipRisk",
            "the control does not intersect any corner box (y=16 is below the 14px corner box)");
    }

    [Fact]
    public void ClipRisk_ControlWithTextInCorner_EmitsError()
    {
        // Same position as the warning test but with text — severity escalates to error
        var form = DefaultForm;
        var root = MakeNode("DashboardForm", "DashboardForm", "", 0, 0, 520, 254, children: [1]);
        var ctrl = MakeNode("Label", "myLabel", "Hello", 490, 5, 20, 12);
        var tree = new List<LayoutNode> { root, ctrl }.AsReadOnly();

        var findings = LayoutAnalyzer.Analyze(form, tree);

        var clipFindings = findings.Where(f => f.Kind == "regionClipRisk").ToList();
        clipFindings.Should().NotBeEmpty("control with text in a corner must emit a finding");
        clipFindings.Should().OnlyContain(f => f.Severity == "error",
            "text-bearing controls in corner zones escalate to error");
    }

    [Fact]
    public void ClipRisk_HiddenControl_IsSkipped()
    {
        var form = DefaultForm;
        var root = MakeNode("DashboardForm", "DashboardForm", "", 0, 0, 520, 254, children: [1]);
        var ctrl = MakeNode("Label", "myLabel", "text", 490, 5, 20, 12, visible: false);
        var tree = new List<LayoutNode> { root, ctrl }.AsReadOnly();

        var findings = LayoutAnalyzer.Analyze(form, tree);

        findings.Should().NotContain(f => f.Kind == "regionClipRisk",
            "hidden controls are excluded from all detectors");
    }

    // ---- siblingOverlap tests ----

    [Fact]
    public void SiblingOverlap_TwoOverlappingPanels_EmitsWarning()
    {
        // parent with two visible children whose bounds overlap
        var form = DefaultForm;
        var root = MakeNode("DashboardForm", "DashboardForm", "", 0, 0, 520, 254, children: [1]);
        var parent = MakeNode("Panel", "container", "", 0, 50, 300, 150, children: [2, 3]);
        var childA = MakeNode("Panel", "pA", "", 0, 0, 100, 50);
        var childB = MakeNode("Panel", "pB", "", 50, 25, 100, 50);
        var tree = new List<LayoutNode> { root, parent, childA, childB }.AsReadOnly();

        var findings = LayoutAnalyzer.Analyze(form, tree);

        findings.Should().ContainSingle(f => f.Kind == "siblingOverlap",
            "the two child panels overlap by 50×25 = 1250px²");
        var finding = findings.First(f => f.Kind == "siblingOverlap");
        finding.Severity.Should().Be("warning");
        finding.Details["overlapArea"].Should().Be("1250");
    }

    [Fact]
    public void SiblingOverlap_NonOverlappingSiblings_NoFinding()
    {
        var form = DefaultForm;
        var root = MakeNode("DashboardForm", "DashboardForm", "", 0, 0, 520, 254, children: [1]);
        var parent = MakeNode("Panel", "container", "", 0, 50, 300, 150, children: [2, 3]);
        var childA = MakeNode("Panel", "pA", "", 0, 0, 100, 50);
        var childB = MakeNode("Panel", "pB", "", 100, 0, 100, 50); // touches but does not overlap
        var tree = new List<LayoutNode> { root, parent, childA, childB }.AsReadOnly();

        var findings = LayoutAnalyzer.Analyze(form, tree);

        findings.Should().NotContain(f => f.Kind == "siblingOverlap");
    }

    [Fact]
    public void SiblingOverlap_AllowListedAccentBarOverHeaderPanel_NoFinding()
    {
        // Panel[accentBar] overlapping Panel[headerPanel] is intentional (decorative stripe)
        var form = DefaultForm;
        var root = MakeNode("DashboardForm", "DashboardForm", "", 0, 0, 520, 254, children: [1]);
        var parent = MakeNode("Panel", "container", "", 0, 0, 520, 60, children: [2, 3]);
        var accentBar = MakeNode("Panel", "accentBar", "", 0, 0, 3, 60);
        var headerPanel = MakeNode("Panel", "headerPanel", "", 0, 0, 520, 60);
        var tree = new List<LayoutNode> { root, parent, accentBar, headerPanel }.AsReadOnly();

        var findings = LayoutAnalyzer.Analyze(form, tree);

        findings.Should().NotContain(f => f.Kind == "siblingOverlap",
            "accentBar/headerPanel overlap is in the allow-list");
    }

    // ---- edgeProximity tests ----

    [Fact]
    public void EdgeProximity_ControlTouchingLeftEdge_EmitsInfo()
    {
        // Left=0 < 4 → proximity to left edge
        var form = DefaultForm;
        var root = MakeNode("DashboardForm", "DashboardForm", "", 0, 0, 520, 254, children: [1]);
        var ctrl = MakeNode("Panel", "sidebar", "", 0, 100, 50, 50);
        var tree = new List<LayoutNode> { root, ctrl }.AsReadOnly();

        var findings = LayoutAnalyzer.Analyze(form, tree);

        var proximity = findings.Where(f => f.Kind == "edgeProximity").ToList();
        proximity.Should().NotBeEmpty("Left=0 is within 4px of the left edge");
        proximity.First().Severity.Should().Be("info");
        proximity.First().Details["edges"].Should().Contain("left");
    }

    // ---- collapsedRow tests ----

    [Fact]
    public void CollapsedRow_TlpWithZeroHeightRow_EmitsInfo()
    {
        // TableLayoutPanel with row[3]=0 → collapsed row finding
        var form = DefaultForm;
        var details = ImmutableDictionary.CreateRange(new[]
        {
            new KeyValuePair<string, string>("row[0]", "40"),
            new KeyValuePair<string, string>("row[1]", "30"),
            new KeyValuePair<string, string>("row[2]", "20"),
            new KeyValuePair<string, string>("row[3]", "0"),
        });
        var root = MakeNode("DashboardForm", "DashboardForm", "", 0, 0, 520, 254, children: [1]);
        var tlp = MakeNode("TableLayoutPanel", "mainTlp", "", 0, 0, 520, 200, details: details);
        var tree = new List<LayoutNode> { root, tlp }.AsReadOnly();

        var findings = LayoutAnalyzer.Analyze(form, tree);

        var collapsed = findings.Where(f => f.Kind == "collapsedRow").ToList();
        collapsed.Should().ContainSingle("only row[3] is 0");
        collapsed[0].Severity.Should().Be("info");
        collapsed[0].Details["rowIndex"].Should().Be("3");
        collapsed[0].Message.Should().Contain("row 3 collapsed");
    }

    // ---- Mixed / ordering tests ----

    [Fact]
    public void Mixed_TreeWithThreeFindingKinds_AllEmittedInOrder()
    {
        // Combine: a control in the corner (clip risk), two overlapping siblings, and a collapsed TLP row.
        var form = DefaultForm;

        var tlpDetails = ImmutableDictionary.CreateRange(new[]
        {
            new KeyValuePair<string, string>("row[0]", "0"),
        });

        // Index layout:
        // 0 = form root
        // 1 = TLP (collapsed row)  — child of root
        // 2 = parent panel         — child of root
        // 3 = sibling A            — child of panel
        // 4 = sibling B (overlaps) — child of panel
        // 5 = corner control       — child of root
        var root = MakeNode("DashboardForm", "DashboardForm", "", 0, 0, 520, 254,
            children: [1, 2, 5]);
        var tlp = MakeNode("TableLayoutPanel", "mainTlp", "", 0, 50, 520, 150,
            details: tlpDetails);
        var parentPanel = MakeNode("Panel", "content", "", 20, 80, 400, 100,
            children: [3, 4]);
        var sibA = MakeNode("Panel", "a", "", 0, 0, 100, 50);
        var sibB = MakeNode("Panel", "b", "", 50, 0, 100, 50); // overlaps a by 50×50=2500
        var cornerCtrl = MakeNode("Label", "corner", "", 505, 2, 20, 10); // intersects top-right

        var tree = new List<LayoutNode>
            { root, tlp, parentPanel, sibA, sibB, cornerCtrl }.AsReadOnly();

        var findings = LayoutAnalyzer.Analyze(form, tree);

        findings.Should().Contain(f => f.Kind == "regionClipRisk");
        findings.Should().Contain(f => f.Kind == "siblingOverlap");
        findings.Should().Contain(f => f.Kind == "collapsedRow");

        // Detector order: clipRisk first, then siblingOverlap, then collapsedRow
        int clipIdx = findings.ToList().FindIndex(f => f.Kind == "regionClipRisk");
        int overlapIdx = findings.ToList().FindIndex(f => f.Kind == "siblingOverlap");
        int collapsedIdx = findings.ToList().FindIndex(f => f.Kind == "collapsedRow");

        clipIdx.Should().BeLessThan(overlapIdx, "clip risk is detected before sibling overlap");
        overlapIdx.Should().BeLessThan(collapsedIdx, "sibling overlap is detected before collapsed rows");
    }

    [Fact]
    public void HiddenControl_SkippedByAllDetectors()
    {
        // A hidden control in a corner, near an edge, overlapping a sibling — none should fire
        var form = DefaultForm;
        var root = MakeNode("DashboardForm", "DashboardForm", "", 0, 0, 520, 254, children: [1, 2]);
        var hidden = MakeNode("Label", "ghost", "text", 490, 5, 20, 12, visible: false);
        var visible = MakeNode("Label", "real", "", 200, 100, 50, 20);
        var tree = new List<LayoutNode> { root, hidden, visible }.AsReadOnly();

        var findings = LayoutAnalyzer.Analyze(form, tree);

        findings.Should().NotContain(f =>
            f.Kind == "regionClipRisk" || f.Kind == "siblingOverlap" ||
            f.Kind == "edgeProximity" || f.Kind == "collapsedRow",
            "hidden controls must be excluded from all detectors");
    }
}
