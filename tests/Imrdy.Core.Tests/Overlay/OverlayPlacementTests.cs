using System.Drawing;
using FluentAssertions;
using Imrdy.Core.Overlay;

namespace Imrdy.Core.Tests.Overlay;

/// <summary>
/// Decision-table tests for <see cref="OverlayPlacement"/>: the null-boundary resolution
/// chain, working-area clamp, edge/corner snap thresholds, and anchor&lt;-&gt;offset
/// round-trip. Fixture: 1000x800 working area at origin (0,0), 100x64 panel unless a test
/// documents a different fixture.
/// </summary>
[Trait("Category", "Unit")]
public class OverlayPlacementTests
{
    private static readonly Rectangle WorkingArea = new(0, 0, 1000, 800);
    private static readonly Size PanelSize = new(100, 50);

    // ---- ResolveOrigin: resolution chain (null-boundary contract) ----
    // Row shape: offsetX, offsetY, positionAnchor -> expected origin.
    // Fixture for this section uses a 1920x1080 working area + 200x64 panel so the
    // default-anchor (Right, Bottom) expectation (margin=16, taskbar reserve=8) matches
    // the documented CalculatePosition formula with round numbers.
    private static readonly Rectangle ResolutionWorkingArea = new(0, 0, 1920, 1080);
    private static readonly Size ResolutionPanelSize = new(200, 64);

    // Row: offset null + valid anchor -> anchored origin (both null; X-only null; Y-only null).
    // Row: offset null + null/blank/garbage anchor -> default (Right, Bottom) anchored origin.
    [Theory]
    [InlineData(null, null, "top-left", 16, 16)]
    [InlineData(300, null, "top-left", 16, 16)]
    [InlineData(null, 400, "top-left", 16, 16)]
    [InlineData(null, null, null, 1704, 1008)]
    [InlineData(null, null, "", 1704, 1008)]
    [InlineData(null, null, "garbage", 1704, 1008)]
    public void ResolveOrigin_EitherOffsetNull_FallsBackToAnchorPath(
        int? offsetX, int? offsetY, string? positionAnchor, int expectedX, int expectedY)
    {
        var origin = OverlayPlacement.ResolveOrigin(
            offsetX, offsetY, positionAnchor, ResolutionWorkingArea, ResolutionPanelSize);

        origin.Should().Be(new Point(expectedX, expectedY));
    }

    // Row: both offsets present -> offset interpreted monitor-relative, ignoring the anchor entirely.
    [Fact]
    public void ResolveOrigin_BothOffsetsPresent_UsesOffsetIgnoringAnchor()
    {
        var origin = OverlayPlacement.ResolveOrigin(
            300, 400, "top-left", ResolutionWorkingArea, ResolutionPanelSize);

        origin.Should().Be(new Point(300, 400));
    }

    // Row: both offsets present but out of bounds -> clamped via ClampToWorkingArea.
    [Fact]
    public void ResolveOrigin_BothOffsetsPresentOutOfBounds_ClampsToWorkingArea()
    {
        var origin = OverlayPlacement.ResolveOrigin(
            5000, 5000, "top-left", ResolutionWorkingArea, ResolutionPanelSize);

        origin.Should().Be(new Point(
            ResolutionWorkingArea.Right - ResolutionPanelSize.Width,
            ResolutionWorkingArea.Bottom - ResolutionPanelSize.Height));
    }

    // ---- ClampToWorkingArea: each edge, both corners, already-inside no-op ----

    [Fact]
    public void ClampToWorkingArea_AlreadyInside_ReturnsUnchanged()
    {
        var origin = new Point(400, 300);

        OverlayPlacement.ClampToWorkingArea(origin, PanelSize, WorkingArea).Should().Be(origin);
    }

    [Fact]
    public void ClampToWorkingArea_PastLeftEdge_ClampsToLeft()
    {
        OverlayPlacement.ClampToWorkingArea(new Point(-500, 300), PanelSize, WorkingArea)
            .Should().Be(new Point(0, 300));
    }

    [Fact]
    public void ClampToWorkingArea_PastRightEdge_ClampsToRight()
    {
        OverlayPlacement.ClampToWorkingArea(new Point(5000, 300), PanelSize, WorkingArea)
            .Should().Be(new Point(900, 300));
    }

    [Fact]
    public void ClampToWorkingArea_PastTopEdge_ClampsToTop()
    {
        OverlayPlacement.ClampToWorkingArea(new Point(400, -500), PanelSize, WorkingArea)
            .Should().Be(new Point(400, 0));
    }

    [Fact]
    public void ClampToWorkingArea_PastBottomEdge_ClampsToBottom()
    {
        OverlayPlacement.ClampToWorkingArea(new Point(400, 5000), PanelSize, WorkingArea)
            .Should().Be(new Point(400, 750));
    }

    [Fact]
    public void ClampToWorkingArea_PastTopLeftCorner_ClampsBothAxes()
    {
        OverlayPlacement.ClampToWorkingArea(new Point(-500, -500), PanelSize, WorkingArea)
            .Should().Be(new Point(0, 0));
    }

    [Fact]
    public void ClampToWorkingArea_PastBottomRightCorner_ClampsBothAxes()
    {
        OverlayPlacement.ClampToWorkingArea(new Point(5000, 5000), PanelSize, WorkingArea)
            .Should().Be(new Point(900, 750));
    }

    // ---- ComputeEdgeSnap: threshold boundaries, 4 edges + 4 corners, default threshold=24 ----
    // distance = threshold - 1 (23) -> snaps flush. Panel is 100x50 inside a 1000x800 area,
    // so the flush right/bottom origins are (900, y)/(x, 750).

    [Theory]
    [InlineData(23, 300, 0, 300)]      // left edge
    [InlineData(877, 300, 900, 300)]   // right edge
    [InlineData(400, 23, 400, 0)]      // top edge
    [InlineData(400, 727, 400, 750)]   // bottom edge
    [InlineData(23, 23, 0, 0)]         // top-left corner
    [InlineData(877, 23, 900, 0)]      // top-right corner
    [InlineData(23, 727, 0, 750)]      // bottom-left corner
    [InlineData(877, 727, 900, 750)]   // bottom-right corner
    public void ComputeEdgeSnap_AtThresholdMinusOne_SnapsFlush(
        int originX, int originY, int expectedX, int expectedY)
    {
        var origin = new Point(originX, originY);

        OverlayPlacement.ComputeEdgeSnap(origin, PanelSize, WorkingArea)
            .Should().Be(new Point(expectedX, expectedY));
    }

    // distance = threshold + 1 (25) -> axis remains unchanged (free-float).
    [Theory]
    [InlineData(25, 300)]      // left edge
    [InlineData(875, 300)]     // right edge
    [InlineData(400, 25)]      // top edge
    [InlineData(400, 725)]     // bottom edge
    [InlineData(25, 25)]       // top-left corner
    [InlineData(875, 25)]      // top-right corner
    [InlineData(25, 725)]      // bottom-left corner
    [InlineData(875, 725)]     // bottom-right corner
    public void ComputeEdgeSnap_AtThresholdPlusOne_RemainsFreeFloat(int originX, int originY)
    {
        var origin = new Point(originX, originY);

        OverlayPlacement.ComputeEdgeSnap(origin, PanelSize, WorkingArea)
            .Should().Be(origin);
    }

    // ---- Anchor <-> offset round-trip: all 6 canonical anchors ----

    [Theory]
    [InlineData("top-left")]
    [InlineData("top-center")]
    [InlineData("top-right")]
    [InlineData("bottom-left")]
    [InlineData("bottom-center")]
    [InlineData("bottom-right")]
    public void AnchorToOffset_ThenResolveOrigin_RoundTripsToPureAnchorOrigin(string anchor)
    {
        var (offsetX, offsetY) = OverlayPlacement.AnchorToOffset(anchor, WorkingArea, PanelSize);

        var viaOffset = OverlayPlacement.ResolveOrigin(offsetX, offsetY, anchor, WorkingArea, PanelSize);
        var viaAnchor = OverlayPlacement.ResolveOrigin(null, null, anchor, WorkingArea, PanelSize);

        viaOffset.Should().Be(viaAnchor);
    }
}
