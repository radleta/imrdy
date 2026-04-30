using FluentAssertions;
using Imrdy.Core.Notifications;

namespace Imrdy.Core.Tests.Notifications;

/// <summary>
/// Covers every cell of the consensus eligibility state matrix.
/// Axes: LastTeammateAt bucket (null / &lt;15s / &gt;=15s) × StatusSince bucket (&lt;90s / &gt;=90s).
/// </summary>
public class ConsensusGateTests
{
    private static readonly TimeSpan QuietThreshold = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MaxDoneTime = TimeSpan.FromSeconds(90);

    // Row 1: LastTeammateAt = null — both columns return false (early return; solo sessions use dwell path).
    [Fact]
    public void IsEligible_NullLastTeammateAt_ReturnsFalse_RegardlessOfStatusSince()
    {
        var now = DateTimeOffset.UtcNow;

        // StatusSince < 90s
        ConsensusGate.IsEligibleForPromotion(null, now.AddSeconds(-80), now, QuietThreshold, MaxDoneTime)
            .Should().BeFalse();

        // StatusSince >= 90s
        ConsensusGate.IsEligibleForPromotion(null, now.AddSeconds(-91), now, QuietThreshold, MaxDoneTime)
            .Should().BeFalse();
    }

    // Row 2, Col 1: LastTeammateAt < 15s (active), StatusSince < 90s → false (regression prevention).
    [Fact]
    public void IsEligible_TeammateActive_StatusFresh_ReturnsFalse()
    {
        var now = DateTimeOffset.UtcNow;
        var lastTeammateAt = now.AddSeconds(-14);   // < 15s — teammates active
        var statusSince = now.AddSeconds(-80);       // < 90s — not yet aged

        ConsensusGate.IsEligibleForPromotion(lastTeammateAt, statusSince, now, QuietThreshold, MaxDoneTime)
            .Should().BeFalse();
    }

    // Row 2, Col 2: LastTeammateAt < 15s (active), StatusSince >= 90s → true (bug fix: bypass fires).
    [Fact]
    public void IsEligible_TeammateActive_StatusAged_ReturnsTrue()
    {
        var now = DateTimeOffset.UtcNow;
        var lastTeammateAt = now.AddSeconds(-14);   // < 15s — teammates still active
        var statusSince = now.AddSeconds(-91);       // >= 90s — session stalled in "done"

        ConsensusGate.IsEligibleForPromotion(lastTeammateAt, statusSince, now, QuietThreshold, MaxDoneTime)
            .Should().BeTrue();
    }

    // Row 3, Col 1: LastTeammateAt >= 15s (quiet), StatusSince < 90s → true (existing quiet-path behavior).
    [Fact]
    public void IsEligible_TeammateQuiet_StatusFresh_ReturnsTrue()
    {
        var now = DateTimeOffset.UtcNow;
        var lastTeammateAt = now.AddSeconds(-16);   // >= 15s — teammates quiet
        var statusSince = now.AddSeconds(-30);       // < 90s — recently entered "done"

        ConsensusGate.IsEligibleForPromotion(lastTeammateAt, statusSince, now, QuietThreshold, MaxDoneTime)
            .Should().BeTrue();
    }

    // Row 3, Col 2: LastTeammateAt >= 15s (quiet), StatusSince >= 90s → true (both gates pass; redundant but correct).
    [Fact]
    public void IsEligible_TeammateQuiet_StatusAged_ReturnsTrue()
    {
        var now = DateTimeOffset.UtcNow;
        var lastTeammateAt = now.AddSeconds(-16);   // >= 15s — teammates quiet
        var statusSince = now.AddSeconds(-91);       // >= 90s — also aged out

        ConsensusGate.IsEligibleForPromotion(lastTeammateAt, statusSince, now, QuietThreshold, MaxDoneTime)
            .Should().BeTrue();
    }
}
