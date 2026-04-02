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
    public void ResolveColor_Start_MapsToBusyRed()
    {
        StatusMap.ResolveColor("start").Should().Be((230, 40, 40));
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
    [InlineData("start", "busy")]
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
}
