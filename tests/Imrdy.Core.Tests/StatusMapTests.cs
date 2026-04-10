using Imrdy.Core.Status;
using FluentAssertions;

namespace Imrdy.Core.Tests;

public class StatusMapTests
{
    [Theory]
    [InlineData("busy", 230, 40, 40)]
    [InlineData("idle", 40, 200, 40)]
    [InlineData("attention", 255, 120, 0)]
    [InlineData("permission", 180, 60, 230)]
    [InlineData("compact", 60, 120, 230)]
    [InlineData("unknown", 128, 128, 128)]
    [InlineData("workspace", 255, 255, 255)]
    public void ResolveColor_BaseStatuses_ReturnCorrectRgb(string status, byte r, byte g, byte b)
    {
        StatusMap.ResolveColor(status).Should().Be((r, g, b));
    }

    [Fact]
    public void ResolveColor_Start_MapsToIdleGreen()
    {
        StatusMap.ResolveColor("start").Should().Be((40, 200, 40));
    }

    [Fact]
    public void ResolveColor_End_MapsToUnknownGray()
    {
        StatusMap.ResolveColor("end").Should().Be((128, 128, 128));
    }

    [Fact]
    public void ResolveColor_UnrecognizedStatus_ReturnsDefaultGray()
    {
        StatusMap.ResolveColor("nonexistent").Should().Be((128, 128, 128));
    }

    [Theory]
    [InlineData("start", "idle")]
    [InlineData("end", "unknown")]
    public void ResolveBaseStatus_HookStatuses_MapToBase(string hook, string expected)
    {
        StatusMap.ResolveBaseStatus(hook).Should().Be(expected);
    }

    [Theory]
    [InlineData("busy")]
    [InlineData("idle")]
    [InlineData("attention")]
    [InlineData("permission")]
    [InlineData("compact")]
    public void ResolveBaseStatus_BaseStatuses_PassThrough(string status)
    {
        StatusMap.ResolveBaseStatus(status).Should().Be(status);
    }

    [Fact]
    public void ResolveColor_CaseInsensitive()
    {
        StatusMap.ResolveColor("BUSY").Should().Be((230, 40, 40));
        StatusMap.ResolveColor("Idle").Should().Be((40, 200, 40));
    }

    [Fact]
    public void KnownBaseStatuses_ContainsAllExpected()
    {
        StatusMap.KnownBaseStatuses.Should().HaveCount(7);
        StatusMap.KnownBaseStatuses.Should().Contain(["busy", "idle", "attention", "permission", "compact", "unknown", "workspace"]);
    }

    [Fact]
    public void GetAgingTier_Under1Minute_Returns0()
    {
        StatusMap.GetAgingTier(TimeSpan.FromSeconds(30)).Should().Be(0);
    }

    [Fact]
    public void GetAgingTier_Between1And3Minutes_Returns1()
    {
        StatusMap.GetAgingTier(TimeSpan.FromMinutes(2)).Should().Be(1);
    }

    [Fact]
    public void GetAgingTier_Between3And7Minutes_Returns2()
    {
        StatusMap.GetAgingTier(TimeSpan.FromMinutes(5)).Should().Be(2);
    }

    [Fact]
    public void GetAgingTier_Between7And15Minutes_Returns3()
    {
        StatusMap.GetAgingTier(TimeSpan.FromMinutes(10)).Should().Be(3);
    }

    [Fact]
    public void GetAgingTier_Over15Minutes_Returns4()
    {
        StatusMap.GetAgingTier(TimeSpan.FromMinutes(20)).Should().Be(4);
    }

    [Fact]
    public void GetAgingTier_AtBoundary_UsesLowerTier()
    {
        // TimeSpan.FromMinutes(1) is exactly 1 minute — must return tier 1 (not 0), since the < 1 branch excludes it
        StatusMap.GetAgingTier(TimeSpan.FromMinutes(1)).Should().Be(1);
    }

    [Fact]
    public void GetAgingFactorFromTier_RoundTripMatchesLegacy()
    {
        // Verify all 5 tiers produce the same factor as CircleIconRenderer.GetAgingFactor at each tier boundary
        StatusMap.GetAgingFactorFromTier(0).Should().Be(1.0);   // < 1m
        StatusMap.GetAgingFactorFromTier(1).Should().Be(0.85);  // 1-3m
        StatusMap.GetAgingFactorFromTier(2).Should().Be(0.70);  // 3-7m
        StatusMap.GetAgingFactorFromTier(3).Should().Be(0.55);  // 7-15m
        StatusMap.GetAgingFactorFromTier(4).Should().Be(0.40);  // 15m+
    }
}
