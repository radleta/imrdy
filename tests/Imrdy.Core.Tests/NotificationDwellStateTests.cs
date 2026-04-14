using Imrdy.Core.Sound;
using FluentAssertions;

namespace Imrdy.Core.Tests;

public class NotificationDwellStateTests
{
    private readonly NotificationDwellState _dwell = new();

    private static readonly DateTimeOffset BaseTime = new(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);

    // --- Dwell behavior ---

    [Fact]
    public void OnStatusChanged_ThenGetFired_BeforeDwell_ReturnsEmpty()
    {
        _dwell.OnStatusChanged("s1", "idle", "busy", BaseTime);

        // idle dwell = 5s; check at +2s (before dwell elapses)
        var fired = _dwell.GetFiredSessions(BaseTime + TimeSpan.FromSeconds(2));

        fired.Should().BeEmpty();
    }

    [Fact]
    public void OnStatusChanged_ThenGetFired_AfterDwell_ReturnsFired()
    {
        _dwell.OnStatusChanged("s1", "idle", "busy", BaseTime);

        // idle dwell = 5s; check at +6s
        var fired = _dwell.GetFiredSessions(BaseTime + TimeSpan.FromSeconds(6));

        fired.Should().HaveCount(1);
        fired[0].SessionId.Should().Be("s1");
        fired[0].Status.Should().Be("idle");
        fired[0].PreviousStatus.Should().Be("busy");
    }

    [Fact]
    public void OnStatusChanged_Twice_SecondReplacesFirst()
    {
        // Set idle at t=0
        _dwell.OnStatusChanged("s1", "idle", "busy", BaseTime);

        // Set busy at t=+1s (replaces idle)
        _dwell.OnStatusChanged("s1", "busy", "idle", BaseTime + TimeSpan.FromSeconds(1));

        // Check at t=+4s: busy dwell=3s → 4-1=3s elapsed ≥ 3s → fires as busy
        var fired = _dwell.GetFiredSessions(BaseTime + TimeSpan.FromSeconds(4));

        fired.Should().HaveCount(1);
        fired[0].Status.Should().Be("busy");
    }

    [Fact]
    public void OnStatusChanged_DifferentSessions_IndependentDwell()
    {
        // s1: busy at t=0 (dwell=3s)
        _dwell.OnStatusChanged("s1", "busy", "idle", BaseTime);

        // s2: idle at t=+2s (dwell=5s)
        _dwell.OnStatusChanged("s2", "idle", "busy", BaseTime + TimeSpan.FromSeconds(2));

        // At t=+4s: s1 has 4s elapsed ≥ 3s → fires; s2 has 2s elapsed < 5s → still pending
        var fired4 = _dwell.GetFiredSessions(BaseTime + TimeSpan.FromSeconds(4));
        fired4.Should().HaveCount(1);
        fired4[0].SessionId.Should().Be("s1");

        // At t=+8s: s2 has 6s elapsed ≥ 5s → fires now
        var fired8 = _dwell.GetFiredSessions(BaseTime + TimeSpan.FromSeconds(8));
        fired8.Should().HaveCount(1);
        fired8[0].SessionId.Should().Be("s2");
    }

    [Fact]
    public void GetFiredSessions_ClearsIsPendingAfterFire()
    {
        // end dwell = 2s
        _dwell.OnStatusChanged("s1", "end", "idle", BaseTime);

        // Fire at +3s → clears IsPending, sets LastNotifiedAt=+3s
        var fired = _dwell.GetFiredSessions(BaseTime + TimeSpan.FromSeconds(3));
        fired.Should().HaveCount(1);

        // Re-arm at +3.5s → IsPending=true, ChangedAt=+3.5s
        _dwell.OnStatusChanged("s1", "end", "idle", BaseTime + TimeSpan.FromSeconds(3.5));

        // Check at +4s: only 0.5s since re-arm, dwell=2s not elapsed → empty
        // (cooldown would not block since 4-3=1s < 10s; the blocker is the unelapsed dwell)
        var firedEarly = _dwell.GetFiredSessions(BaseTime + TimeSpan.FromSeconds(4));
        firedEarly.Should().BeEmpty();
    }

    // --- Toast cooldown ---

    [Fact]
    public void GetFiredSessions_AfterFire_CooldownPreventsSecondFire()
    {
        // Fire first time
        _dwell.OnStatusChanged("s1", "busy", "idle", BaseTime);
        _dwell.GetFiredSessions(BaseTime + TimeSpan.FromSeconds(4)); // fires at +4s

        // Re-arm immediately after fire
        _dwell.OnStatusChanged("s1", "busy", "idle", BaseTime + TimeSpan.FromSeconds(4));

        // Check at +8s: dwell elapsed (3s since +4s re-arm), but cooldown blocks (only 4s since last toast at +4s < 10s)
        var blocked = _dwell.GetFiredSessions(BaseTime + TimeSpan.FromSeconds(8));
        blocked.Should().BeEmpty();
    }

    [Fact]
    public void GetFiredSessions_AfterCooldownExpires_FiresAgain()
    {
        // Fire first time at +4s
        _dwell.OnStatusChanged("s1", "busy", "idle", BaseTime);
        _dwell.GetFiredSessions(BaseTime + TimeSpan.FromSeconds(4));

        // Re-arm at +4s
        _dwell.OnStatusChanged("s1", "busy", "idle", BaseTime + TimeSpan.FromSeconds(4));

        // Check at +15s: dwell elapsed (11s since re-arm ≥ 3s), cooldown expired (11s since last toast ≥ 10s)
        var fired = _dwell.GetFiredSessions(BaseTime + TimeSpan.FromSeconds(15));
        fired.Should().HaveCount(1);
        fired[0].SessionId.Should().Be("s1");
    }

    // --- Session lifecycle ---

    [Fact]
    public void RemoveSession_CancelsPending()
    {
        _dwell.OnStatusChanged("s1", "idle", "busy", BaseTime);
        _dwell.RemoveSession("s1");

        var fired = _dwell.GetFiredSessions(BaseTime + TimeSpan.FromSeconds(10));
        fired.Should().BeEmpty();
    }

    [Fact]
    public void RemoveSession_NonExistent_NoOp()
    {
        // Must not throw
        var act = () => _dwell.RemoveSession("nonexistent");
        act.Should().NotThrow();
    }

    [Fact]
    public void Clear_RemovesAllPending()
    {
        _dwell.OnStatusChanged("s1", "idle", "busy", BaseTime);
        _dwell.OnStatusChanged("s2", "busy", "idle", BaseTime);
        _dwell.Clear();

        var fired = _dwell.GetFiredSessions(BaseTime + TimeSpan.FromSeconds(10));
        fired.Should().BeEmpty();
    }

    // --- Dwell durations ---

    [Fact]
    public void DwellDuration_IdleIs5s()
    {
        _dwell.OnStatusChanged("s1", "idle", "busy", BaseTime);

        // Not fired at +4.9s
        _dwell.GetFiredSessions(BaseTime + TimeSpan.FromSeconds(4.9)).Should().BeEmpty();

        // Fired at +5.1s
        _dwell.GetFiredSessions(BaseTime + TimeSpan.FromSeconds(5.1)).Should().HaveCount(1);
    }

    [Fact]
    public void DwellDuration_BusyIs3s()
    {
        _dwell.OnStatusChanged("s1", "busy", "idle", BaseTime);

        // Not fired at +2.9s
        _dwell.GetFiredSessions(BaseTime + TimeSpan.FromSeconds(2.9)).Should().BeEmpty();

        // Fired at +3.1s
        _dwell.GetFiredSessions(BaseTime + TimeSpan.FromSeconds(3.1)).Should().HaveCount(1);
    }

    [Fact]
    public void DwellDuration_EndIs2s()
    {
        _dwell.OnStatusChanged("s1", "end", "idle", BaseTime);

        // Not fired at +1.9s
        _dwell.GetFiredSessions(BaseTime + TimeSpan.FromSeconds(1.9)).Should().BeEmpty();

        // Fired at +2.1s
        _dwell.GetFiredSessions(BaseTime + TimeSpan.FromSeconds(2.1)).Should().HaveCount(1);
    }

    [Fact]
    public void DwellDuration_UnknownStatusUses3sDefault()
    {
        _dwell.OnStatusChanged("s1", "foo", "idle", BaseTime);

        // Not fired at +2.9s
        _dwell.GetFiredSessions(BaseTime + TimeSpan.FromSeconds(2.9)).Should().BeEmpty();

        // Fired at +3.1s
        _dwell.GetFiredSessions(BaseTime + TimeSpan.FromSeconds(3.1)).Should().HaveCount(1);
    }

    // --- FiredNotification fields ---

    [Fact]
    public void FiredNotification_IncludesPreviousStatus()
    {
        _dwell.OnStatusChanged("s1", "idle", "permission", BaseTime);

        var fired = _dwell.GetFiredSessions(BaseTime + TimeSpan.FromSeconds(6));

        fired.Should().HaveCount(1);
        fired[0].PreviousStatus.Should().Be("permission");
    }

    [Fact]
    public void FiredNotification_IncludesNotificationType()
    {
        // With explicit notificationType
        _dwell.OnStatusChanged("s1", "busy", "idle", BaseTime, notificationType: "interrupt");
        var firedWithType = _dwell.GetFiredSessions(BaseTime + TimeSpan.FromSeconds(4));
        firedWithType.Should().HaveCount(1);
        firedWithType[0].NotificationType.Should().Be("interrupt");

        // Without notificationType (status-change entry)
        _dwell.OnStatusChanged("s2", "idle", "busy", BaseTime);
        var firedWithoutType = _dwell.GetFiredSessions(BaseTime + TimeSpan.FromSeconds(6));
        firedWithoutType.Should().HaveCount(1);
        firedWithoutType[0].NotificationType.Should().BeNull();
    }
}
