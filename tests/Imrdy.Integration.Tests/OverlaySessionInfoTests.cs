using FluentAssertions;
using Imrdy.Windows.Overlay;
using Xunit;

namespace Imrdy.Integration.Tests;

[Trait("Category", "Integration")]
public class OverlaySessionInfoTests
{
    [Fact]
    public void Constructor_FourParams_AllPropertiesAssigned()
    {
        var info = new OverlaySessionInfo("sess-1", "busy", 2, "squares");

        info.SessionId.Should().Be("sess-1");
        info.Status.Should().Be("busy");
        info.AgingTier.Should().Be(2);
        info.IconStyle.Should().Be("squares");
    }

    [Fact]
    public void Constructor_IconStyleCircles_Stored()
    {
        var info = new OverlaySessionInfo("s", "idle", 0, "circles");

        info.IconStyle.Should().Be("circles");
    }

    [Theory]
    [InlineData("circles")]
    [InlineData("squares")]
    [InlineData("triangles")]
    [InlineData("diamonds")]
    [InlineData("hexagons")]
    [InlineData("plus")]
    public void Constructor_BuiltInStyles_Roundtrip(string style)
    {
        var info = new OverlaySessionInfo("x", "idle", 0, style);

        info.IconStyle.Should().Be(style);
    }

    [Fact]
    public void Equality_SameParams_AreEqual()
    {
        var a = new OverlaySessionInfo("s", "idle", 0, "circles");
        var b = new OverlaySessionInfo("s", "idle", 0, "circles");

        a.Should().Be(b);
    }

    [Fact]
    public void Equality_DifferentIconStyle_NotEqual()
    {
        var a = new OverlaySessionInfo("s", "idle", 0, "circles");
        var b = new OverlaySessionInfo("s", "idle", 0, "squares");

        a.Should().NotBe(b);
    }
}
