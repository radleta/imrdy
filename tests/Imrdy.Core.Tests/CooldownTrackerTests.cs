using Imrdy.Core.Sound;
using FluentAssertions;

namespace Imrdy.Core.Tests;

public class CooldownTrackerTests
{
    private readonly CooldownTracker _tracker = new(
        cooldownDuration: TimeSpan.FromSeconds(5),
        comboWindow: TimeSpan.FromSeconds(3));

    private static readonly DateTimeOffset BaseTime = new(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IsOnCooldown_NewSession_NotOnCooldown()
    {
        _tracker.IsOnCooldown("session1", BaseTime).Should().BeFalse();
    }

    [Fact]
    public void IsOnCooldown_WithinCooldown_ReturnsTrue()
    {
        _tracker.RecordAndCheckCombo("session1", BaseTime);
        _tracker.IsOnCooldown("session1", BaseTime + TimeSpan.FromSeconds(3)).Should().BeTrue();
    }

    [Fact]
    public void IsOnCooldown_AfterCooldown_ReturnsFalse()
    {
        _tracker.RecordAndCheckCombo("session1", BaseTime);
        _tracker.IsOnCooldown("session1", BaseTime + TimeSpan.FromSeconds(6)).Should().BeFalse();
    }

    [Fact]
    public void IsOnCooldown_ExactCooldownBoundary_ReturnsFalse()
    {
        _tracker.RecordAndCheckCombo("session1", BaseTime);
        _tracker.IsOnCooldown("session1", BaseTime + TimeSpan.FromSeconds(5)).Should().BeFalse();
    }

    [Fact]
    public void RecordAndCheckCombo_SingleSession_NoCombo()
    {
        _tracker.RecordAndCheckCombo("session1", BaseTime).Should().BeFalse();
    }

    [Fact]
    public void RecordAndCheckCombo_TwoSessionsWithinWindow_Combo()
    {
        _tracker.RecordAndCheckCombo("session1", BaseTime);
        _tracker.RecordAndCheckCombo("session2", BaseTime + TimeSpan.FromSeconds(2))
            .Should().BeTrue();
    }

    [Fact]
    public void RecordAndCheckCombo_TwoSessionsOutsideWindow_NoCombo()
    {
        _tracker.RecordAndCheckCombo("session1", BaseTime);
        _tracker.RecordAndCheckCombo("session2", BaseTime + TimeSpan.FromSeconds(4))
            .Should().BeFalse();
    }

    [Fact]
    public void RecordAndCheckCombo_ThreeSessionsWithinWindow_ComboOnSecond()
    {
        _tracker.RecordAndCheckCombo("session1", BaseTime).Should().BeFalse();
        // Combo fires on second distinct session
        _tracker.RecordAndCheckCombo("session2", BaseTime + TimeSpan.FromSeconds(1))
            .Should().BeTrue();
        // After combo reset, third session alone doesn't combo
        _tracker.RecordAndCheckCombo("session3", BaseTime + TimeSpan.FromSeconds(2))
            .Should().BeFalse();
    }

    [Fact]
    public void RemoveSession_ClearsCooldownForThatSession()
    {
        _tracker.RecordAndCheckCombo("session1", BaseTime);
        _tracker.RemoveSession("session1");
        _tracker.IsOnCooldown("session1", BaseTime + TimeSpan.FromSeconds(1)).Should().BeFalse();
    }

    [Fact]
    public void Clear_RemovesAllState()
    {
        _tracker.RecordAndCheckCombo("session1", BaseTime);
        _tracker.RecordAndCheckCombo("session2", BaseTime + TimeSpan.FromSeconds(1));
        _tracker.Clear();

        _tracker.IsOnCooldown("session1", BaseTime + TimeSpan.FromSeconds(1)).Should().BeFalse();
        _tracker.RecordAndCheckCombo("session3", BaseTime + TimeSpan.FromSeconds(2))
            .Should().BeFalse();
    }

    [Fact]
    public void IsOnCooldown_DifferentSessions_Independent()
    {
        _tracker.RecordAndCheckCombo("session1", BaseTime);
        _tracker.IsOnCooldown("session2", BaseTime + TimeSpan.FromSeconds(1)).Should().BeFalse();
    }

    [Fact]
    public void RecordAndCheckCombo_SameSessionTwice_NoCombo()
    {
        _tracker.RecordAndCheckCombo("session1", BaseTime);
        _tracker.RecordAndCheckCombo("session1", BaseTime + TimeSpan.FromSeconds(2))
            .Should().BeFalse();
    }

    [Fact]
    public void RecordAndCheckCombo_ComboResetsAfterFiring()
    {
        _tracker.RecordAndCheckCombo("session1", BaseTime);
        _tracker.RecordAndCheckCombo("session2", BaseTime + TimeSpan.FromSeconds(1))
            .Should().BeTrue();

        // After combo fires, a single session should not trigger another combo
        _tracker.RecordAndCheckCombo("session3", BaseTime + TimeSpan.FromSeconds(2))
            .Should().BeFalse();
    }

    [Fact]
    public void RecordAndCheckCombo_NewBurstAfterReset_FiresComboAgain()
    {
        // First burst
        _tracker.RecordAndCheckCombo("session1", BaseTime);
        _tracker.RecordAndCheckCombo("session2", BaseTime + TimeSpan.FromSeconds(1))
            .Should().BeTrue();

        // Second burst (new pair within window)
        _tracker.RecordAndCheckCombo("session3", BaseTime + TimeSpan.FromSeconds(2));
        _tracker.RecordAndCheckCombo("session4", BaseTime + TimeSpan.FromSeconds(2.5))
            .Should().BeTrue();
    }
}
