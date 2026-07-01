using FluentAssertions;
using Imrdy.Core.Overlay;

namespace Imrdy.Core.Tests.Overlay;

[Trait("Category", "Unit")]
public class OverlayAnchorTests
{
    // ---- Parse: all 6 canonical strings ----

    [Theory]
    [InlineData("top-left",      HorizontalAnchor.Left,   VerticalAnchor.Top)]
    [InlineData("top-center",    HorizontalAnchor.Center, VerticalAnchor.Top)]
    [InlineData("top-right",     HorizontalAnchor.Right,  VerticalAnchor.Top)]
    [InlineData("bottom-left",   HorizontalAnchor.Left,   VerticalAnchor.Bottom)]
    [InlineData("bottom-center", HorizontalAnchor.Center, VerticalAnchor.Bottom)]
    [InlineData("bottom-right",  HorizontalAnchor.Right,  VerticalAnchor.Bottom)]
    public void Parse_CanonicalString_ReturnsExpectedEnumPair(
        string position, HorizontalAnchor expectedH, VerticalAnchor expectedV)
    {
        var result = OverlayAnchor.Parse(position);
        result.Horizontal.Should().Be(expectedH);
        result.Vertical.Should().Be(expectedV);
    }

    // ---- Parse: case-insensitivity ----

    [Fact]
    public void Parse_UpperCase_ReturnsExpectedEnumPair()
    {
        var result = OverlayAnchor.Parse("TOP-CENTER");
        result.Horizontal.Should().Be(HorizontalAnchor.Center);
        result.Vertical.Should().Be(VerticalAnchor.Top);
    }

    [Fact]
    public void Parse_MixedCase_ReturnsExpectedEnumPair()
    {
        var result = OverlayAnchor.Parse("Bottom-Right");
        result.Horizontal.Should().Be(HorizontalAnchor.Right);
        result.Vertical.Should().Be(VerticalAnchor.Bottom);
    }

    // ---- Parse: null / blank / garbage → (Right, Bottom) ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage")]
    [InlineData("invalid-anchor")]
    [InlineData("left-bottom")]  // wrong order (not a canonical key)
    public void Parse_NullBlankGarbage_ReturnsDefaultRightBottom(string? position)
    {
        var result = OverlayAnchor.Parse(position);
        result.Horizontal.Should().Be(HorizontalAnchor.Right);
        result.Vertical.Should().Be(VerticalAnchor.Bottom);
    }

    // ---- Round-trip: Parse(s).ToConfigString() == s for all 6 ----

    [Theory]
    [InlineData("top-left")]
    [InlineData("top-center")]
    [InlineData("top-right")]
    [InlineData("bottom-left")]
    [InlineData("bottom-center")]
    [InlineData("bottom-right")]
    public void Parse_ThenToConfigString_RoundTripsCanonicalString(string canonical)
    {
        OverlayAnchor.Parse(canonical).ToConfigString().Should().Be(canonical);
    }
}
