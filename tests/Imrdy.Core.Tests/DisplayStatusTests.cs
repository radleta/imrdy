using FluentAssertions;
using Imrdy.Core.Status;
using Xunit;

namespace Imrdy.Core.Tests;

public class DisplayStatusTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset AgoSeconds(double seconds) => Now - TimeSpan.FromSeconds(seconds);

    [Theory]
    [InlineData("busy")]
    [InlineData("error")]
    [InlineData("permission")]
    [InlineData("attention")]
    [InlineData("compact")]
    [InlineData("start")]
    [InlineData("end")]
    [InlineData("done")]
    public void Resolve_NonIdleStatus_PassesThroughEvenWithFreshAgents(string status)
    {
        DisplayStatus.Resolve(status, AgoSeconds(1), Now).Should().Be(status);
    }

    [Fact]
    public void Resolve_IdleWithNoAgentsEverSeen_StaysIdle()
    {
        DisplayStatus.Resolve("idle", null, Now).Should().Be("idle");
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(15)]
    [InlineData(60)]
    [InlineData(119)]
    public void Resolve_IdleWithRecentAgentActivity_ShowsTeal(double secondsAgo)
    {
        DisplayStatus.Resolve("idle", AgoSeconds(secondsAgo), Now).Should().Be("done");
    }

    [Theory]
    [InlineData(121)]
    [InlineData(600)]
    [InlineData(86_400)]
    public void Resolve_IdleWithStaleAgentActivity_ShowsGreen(double secondsAgo)
    {
        DisplayStatus.Resolve("idle", AgoSeconds(secondsAgo), Now).Should().Be("idle");
    }

    [Fact]
    public void Resolve_AtExactlyTheTimeout_ShowsGreen()
    {
        var boundary = Now - DisplayStatus.TeammatePresenceTimeout;

        DisplayStatus.Resolve("idle", boundary, Now).Should().Be("idle");
    }

    [Fact]
    public void Resolve_JustInsideTheTimeout_ShowsTeal()
    {
        var inside = Now - DisplayStatus.TeammatePresenceTimeout + TimeSpan.FromMilliseconds(1);

        DisplayStatus.Resolve("idle", inside, Now).Should().Be("done");
    }

    [Fact]
    public void Resolve_IsCaseInsensitiveOnStatus()
    {
        DisplayStatus.Resolve("IDLE", AgoSeconds(5), Now).Should().Be("done");
    }

    [Fact]
    public void Resolve_FutureTimestamp_ShowsTeal()
    {
        // Clock skew between the hook process and the tray must not read as "long ago".
        DisplayStatus.Resolve("idle", Now + TimeSpan.FromSeconds(30), Now).Should().Be("done");
    }

    [Fact]
    public void IsIdleWithAgentsRunning_TrueOnlyForIdleLeadWithLiveAgents()
    {
        DisplayStatus.IsIdleWithAgentsRunning("idle", AgoSeconds(5), Now).Should().BeTrue();
        DisplayStatus.IsIdleWithAgentsRunning("idle", AgoSeconds(300), Now).Should().BeFalse();
        DisplayStatus.IsIdleWithAgentsRunning("idle", null, Now).Should().BeFalse();
        DisplayStatus.IsIdleWithAgentsRunning("busy", AgoSeconds(5), Now).Should().BeFalse();
    }
}
