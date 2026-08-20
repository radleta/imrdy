using Imrdy.Core.Hooks;
using Imrdy.Core.Status;
using Imrdy.Core.State;
using FluentAssertions;

namespace Imrdy.Core.Tests;

public class TeammateGateTests
{
    private static StateFileModel CreateState(string status, string notificationType = "") => new()
    {
        SessionId = "test-session",
        Status = status,
        Project = "test",
        Cwd = @"D:\dev\test",
        HookEvent = "Notification",
        NotificationType = notificationType,
        Timestamp = DateTimeOffset.UtcNow,
    };

    // --- ShouldClearPermission ---

    [Theory]
    [InlineData("PostToolUse")]
    [InlineData("PostToolUseFailure")]
    [InlineData("PermissionDenied")]
    public void ShouldClearPermission_PermissionStatus_ResolutionEvent_ReturnsTrue(string eventName)
    {
        TeammateGate.ShouldClearPermission("permission", eventName).Should().BeTrue();
    }

    [Theory]
    [InlineData("PreToolUse")]
    [InlineData("UserPromptSubmit")]
    [InlineData("Stop")]
    [InlineData("Notification")]
    [InlineData("SessionStart")]
    [InlineData("SubagentStart")]
    public void ShouldClearPermission_PermissionStatus_NonResolutionEvent_ReturnsFalse(string eventName)
    {
        TeammateGate.ShouldClearPermission("permission", eventName).Should().BeFalse();
    }

    [Theory]
    [InlineData("busy")]
    [InlineData("idle")]
    [InlineData("done")]
    [InlineData("error")]
    [InlineData("attention")]
    [InlineData("start")]
    public void ShouldClearPermission_NonPermissionStatus_ReturnsFalse(string status)
    {
        TeammateGate.ShouldClearPermission(status, "PostToolUse").Should().BeFalse();
    }

    [Fact]
    public void ShouldClearPermission_NullStatus_ReturnsFalse()
    {
        TeammateGate.ShouldClearPermission(null, "PostToolUse").Should().BeFalse();
    }

    [Fact]
    public void ShouldClearPermission_CaseInsensitiveEventName()
    {
        TeammateGate.ShouldClearPermission("permission", "posttooluse").Should().BeTrue();
        TeammateGate.ShouldClearPermission("permission", "POSTTOOLUSE").Should().BeTrue();
        TeammateGate.ShouldClearPermission("permission", "permissiondenied").Should().BeTrue();
    }

    // --- ApplyTeammateEvent: permission clearing ---

    [Fact]
    public void ApplyTeammateEvent_PostToolUse_ClearsPermission_ToBusy()
    {
        var existing = CreateState("permission", "permission_prompt");

        var result = TeammateGate.ApplyTeammateEvent(existing, "PostToolUse");

        result.Status.Should().Be("busy");
        result.NotificationType.Should().BeEmpty();
        result.LastTeammateAt.Should().NotBeNull();
    }

    [Fact]
    public void ApplyTeammateEvent_PostToolUseFailure_ClearsPermission_ToError()
    {
        var existing = CreateState("permission", "permission_prompt");

        var result = TeammateGate.ApplyTeammateEvent(existing, "PostToolUseFailure");

        result.Status.Should().Be("error");
        result.NotificationType.Should().BeEmpty();
    }

    [Fact]
    public void ApplyTeammateEvent_PermissionDenied_ClearsPermission_ToIdle()
    {
        var existing = CreateState("permission", "permission_prompt");

        var result = TeammateGate.ApplyTeammateEvent(existing, "PermissionDenied");

        result.Status.Should().Be("idle");
        result.NotificationType.Should().BeEmpty();
    }

    // --- ApplyTeammateEvent: non-permission preservation ---

    [Theory]
    [InlineData("busy")]
    [InlineData("done")]
    [InlineData("error")]
    public void ApplyTeammateEvent_NonIdleNonPermission_PreservesStatus(string existingStatus)
    {
        var existing = CreateState(existingStatus);

        var result = TeammateGate.ApplyTeammateEvent(existing, "PostToolUse");

        result.Status.Should().Be(existingStatus);
    }

    [Fact]
    public void ApplyTeammateEvent_Permission_NonResolutionEvent_PreservesPermission()
    {
        var existing = CreateState("permission", "permission_prompt");

        var result = TeammateGate.ApplyTeammateEvent(existing, "PreToolUse");

        result.Status.Should().Be("permission");
        result.NotificationType.Should().Be("permission_prompt");
    }

    // --- Lead-status isolation: subagent activity must never move the lead ---
    // Regression guard. Subagent PreToolUse used to promote an idle lead to "busy", which
    // clobbered the lead's own "waiting for the user" signal within milliseconds. Background
    // agents keep working after the lead has returned control, so subagent activity carries
    // no information about lead readiness.

    [Theory]
    [InlineData("start", "PreToolUse")]
    [InlineData("idle", "PreToolUse")]
    [InlineData("idle", "PostToolUse")]
    [InlineData("idle", "SubagentStart")]
    [InlineData("idle", "SubagentStop")]
    [InlineData("idle", "UserPromptSubmit")]
    [InlineData("idle", "Stop")]
    [InlineData("idle", "TaskCreated")]
    [InlineData("idle", "TaskCompleted")]
    [InlineData("idle", "TeammateIdle")]
    [InlineData("busy", "PreToolUse")]
    [InlineData("done", "PreToolUse")]
    [InlineData("error", "PreToolUse")]
    [InlineData("compact", "PreToolUse")]
    public void ApplyTeammateEvent_NeverChangesLeadStatus(string status, string eventName)
    {
        var existing = CreateState(status);

        var result = TeammateGate.ApplyTeammateEvent(existing, eventName);

        result.Status.Should().Be(status);
    }

    [Fact]
    public void ApplyTeammateEvent_IdleLead_SubagentToolUse_StaysIdle()
    {
        var existing = CreateState("idle") with { NotificationType = "idle_prompt" };

        var result = TeammateGate.ApplyTeammateEvent(existing, "PreToolUse");

        result.Status.Should().Be("idle");
        result.NotificationType.Should().Be("idle_prompt");
    }

    // --- Liveness window: only ongoing activity refreshes last_teammate_at ---
    // A terminal event marks work ENDING. Refreshing on it holds the session teal for a further
    // 2 minutes past the last agent's actual finish, and a stray SubagentStop (observed with an
    // empty agent_type and no matching SubagentStart) would invent agent activity outright.

    [Theory]
    [InlineData("PreToolUse")]
    [InlineData("PostToolUse")]
    [InlineData("PostToolUseFailure")]
    [InlineData("SubagentStart")]
    [InlineData("UserPromptSubmit")]
    [InlineData("TaskCreated")]
    public void ApplyTeammateEvent_OngoingActivity_RefreshesLivenessWindow(string eventName)
    {
        var stale = DateTimeOffset.UtcNow.AddMinutes(-5);
        var existing = CreateState("idle") with { LastTeammateAt = stale };

        var result = TeammateGate.ApplyTeammateEvent(existing, eventName);

        result.LastTeammateAt.Should().NotBeNull();
        result.LastTeammateAt!.Value.Should().BeAfter(stale);
    }

    [Theory]
    [InlineData("SubagentStop")]
    [InlineData("TaskCompleted")]
    [InlineData("TeammateIdle")]
    [InlineData("Stop")]
    public void ApplyTeammateEvent_TerminalEvent_LeavesLivenessWindowUntouched(string eventName)
    {
        var stale = DateTimeOffset.UtcNow.AddMinutes(-5);
        var existing = CreateState("idle") with { LastTeammateAt = stale };

        var result = TeammateGate.ApplyTeammateEvent(existing, eventName);

        result.LastTeammateAt.Should().Be(stale);
        result.Timestamp.Should().BeAfter(stale);   // the file still changed
    }

    [Fact]
    public void ApplyTeammateEvent_StraySubagentStop_DoesNotInventAgentActivity()
    {
        // The observed phantom: SubagentStop with no prior subagent activity on the session.
        // It must not make a genuinely free session look like it has agents running.
        var existing = CreateState("idle") with { LastTeammateAt = null };

        var result = TeammateGate.ApplyTeammateEvent(existing, "SubagentStop");

        result.LastTeammateAt.Should().BeNull();
        DisplayStatus.Resolve(result.Status, result.LastTeammateAt, DateTimeOffset.UtcNow)
            .Should().Be("idle");
    }

    [Theory]
    [InlineData("PreToolUse", true)]
    [InlineData("PostToolUse", true)]
    [InlineData("SubagentStart", true)]
    [InlineData("SubagentStop", false)]
    [InlineData("TaskCompleted", false)]
    [InlineData("TeammateIdle", false)]
    [InlineData("Stop", false)]
    public void IsOngoingActivity_ClassifiesByWhetherWorkIsHappening(string eventName, bool ongoing)
    {
        TeammateGate.IsOngoingActivity(eventName).Should().Be(ongoing);
    }

    // --- IsSubagentLifecycleEvent ---

    [Theory]
    [InlineData("SubagentStart")]
    [InlineData("SubagentStop")]
    [InlineData("TaskCreated")]
    [InlineData("TaskCompleted")]
    [InlineData("TeammateIdle")]
    [InlineData("subagentstop")]
    public void IsSubagentLifecycleEvent_LifecycleEvents_ReturnsTrue(string eventName)
    {
        TeammateGate.IsSubagentLifecycleEvent(eventName).Should().BeTrue();
    }

    [Theory]
    [InlineData("Stop")]
    [InlineData("PreToolUse")]
    [InlineData("PostToolUse")]
    [InlineData("UserPromptSubmit")]
    [InlineData("Notification")]
    [InlineData("SessionStart")]
    [InlineData("SessionEnd")]
    public void IsSubagentLifecycleEvent_LeadReadinessEvents_ReturnsFalse(string eventName)
    {
        TeammateGate.IsSubagentLifecycleEvent(eventName).Should().BeFalse();
    }

    // --- ApplyTeammateEvent: timestamp updates ---

    [Fact]
    public void ApplyTeammateEvent_AlwaysUpdatesTimestamps()
    {
        var oldTime = DateTimeOffset.UtcNow.AddMinutes(-5);
        var existing = CreateState("busy") with
        {
            Timestamp = oldTime,
            LastTeammateAt = oldTime,
        };

        var result = TeammateGate.ApplyTeammateEvent(existing, "PreToolUse");

        result.Timestamp.Should().BeAfter(oldTime);
        result.LastTeammateAt.Should().NotBeNull();
        result.LastTeammateAt!.Value.Should().BeAfter(oldTime);
    }

    [Fact]
    public void ApplyTeammateEvent_PreservesOtherFields()
    {
        var existing = CreateState("busy") with
        {
            SoundPack = "Portal Turret",
            IconStyle = "hexagons",
            DesktopIndex = 2,
            LastMessage = "hello",
        };

        var result = TeammateGate.ApplyTeammateEvent(existing, "PreToolUse");

        result.SoundPack.Should().Be("Portal Turret");
        result.IconStyle.Should().Be("hexagons");
        result.DesktopIndex.Should().Be(2);
        result.LastMessage.Should().Be("hello");
        result.SessionId.Should().Be("test-session");
    }
}
