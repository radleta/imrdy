using Imrdy.Core.Hooks;
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

    // --- ShouldPromoteToBusy ---

    [Theory]
    [InlineData("start", "PreToolUse")]
    [InlineData("start", "PostToolUse")]
    [InlineData("idle", "PreToolUse")]
    [InlineData("idle", "PostToolUse")]
    [InlineData("idle", "SubagentStart")]
    [InlineData("start", "UserPromptSubmit")]
    public void ShouldPromoteToBusy_IdleLeadWithBusyEvent_ReturnsTrue(string status, string eventName)
    {
        TeammateGate.ShouldPromoteToBusy(status, eventName).Should().BeTrue();
    }

    [Theory]
    [InlineData("busy", "PreToolUse")]
    [InlineData("done", "PreToolUse")]
    [InlineData("error", "PreToolUse")]
    [InlineData("permission", "PreToolUse")]
    [InlineData("compact", "PreToolUse")]
    public void ShouldPromoteToBusy_NonIdleLead_ReturnsFalse(string status, string eventName)
    {
        TeammateGate.ShouldPromoteToBusy(status, eventName).Should().BeFalse();
    }

    [Theory]
    [InlineData("start", "Stop")]
    [InlineData("idle", "Notification")]
    [InlineData("start", "SessionEnd")]
    public void ShouldPromoteToBusy_IdleLeadWithNonBusyEvent_ReturnsFalse(string status, string eventName)
    {
        TeammateGate.ShouldPromoteToBusy(status, eventName).Should().BeFalse();
    }

    [Fact]
    public void ShouldPromoteToBusy_NullStatus_ReturnsFalse()
    {
        TeammateGate.ShouldPromoteToBusy(null, "PreToolUse").Should().BeFalse();
    }

    // --- ApplyTeammateEvent: busy promotion ---

    [Fact]
    public void ApplyTeammateEvent_StartLead_TeammateToolUse_PromotesToBusy()
    {
        var existing = CreateState("start");

        var result = TeammateGate.ApplyTeammateEvent(existing, "PreToolUse");

        result.Status.Should().Be("busy");
    }

    [Fact]
    public void ApplyTeammateEvent_IdleLead_TeammateToolUse_PromotesToBusy()
    {
        var existing = CreateState("idle");

        var result = TeammateGate.ApplyTeammateEvent(existing, "PostToolUse");

        result.Status.Should().Be("busy");
    }

    [Fact]
    public void ApplyTeammateEvent_DoneLead_TeammateToolUse_PreservesDone()
    {
        var existing = CreateState("done");

        var result = TeammateGate.ApplyTeammateEvent(existing, "PreToolUse");

        result.Status.Should().Be("done");
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
