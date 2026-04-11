using FluentAssertions;
using Imrdy.Core.Icons;

namespace Imrdy.Core.Tests;

public class StyleNamesTests
{
    [Fact]
    public void NormalizeStyleName_Dots_ReturnsCircles()
    {
        StyleNames.NormalizeStyleName("dots").Should().Be("circles");
    }

    [Fact]
    public void NormalizeStyleName_DotsUpperCase_ReturnsCircles()
    {
        StyleNames.NormalizeStyleName("DOTS").Should().Be("circles");
    }

    [Fact]
    public void NormalizeStyleName_Null_ReturnsNull()
    {
        StyleNames.NormalizeStyleName(null).Should().BeNull();
    }

    [Fact]
    public void NormalizeStyleName_Empty_ReturnsNull()
    {
        StyleNames.NormalizeStyleName("").Should().BeNull();
    }

    [Fact]
    public void NormalizeStyleName_Squares_PassesThrough()
    {
        StyleNames.NormalizeStyleName("squares").Should().Be("squares");
    }

    [Fact]
    public void NormalizeStyleName_PackPrefix_PassesThrough()
    {
        StyleNames.NormalizeStyleName("pack:foo").Should().Be("pack:foo");
    }

    [Fact]
    public void BuiltInStyles_HasExactlySixItems()
    {
        StyleNames.BuiltInStyles.Should().HaveCount(6);
    }

    [Theory]
    [InlineData("circles")]
    [InlineData("squares")]
    [InlineData("triangles")]
    [InlineData("diamonds")]
    [InlineData("hexagons")]
    [InlineData("plus")]
    public void BuiltInStyles_ContainsExpectedNames(string name)
    {
        StyleNames.BuiltInStyles.Should().Contain(name);
    }
}
