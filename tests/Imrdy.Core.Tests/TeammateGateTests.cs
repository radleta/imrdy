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

        var result = TeammateGate.ApplyTeammateEvent(existing, "PostToolUse", null);

        result.Status.Should().Be("busy");
        result.NotificationType.Should().BeEmpty();
    }

    [Fact]
    public void ApplyTeammateEvent_PostToolUseFailure_ClearsPermission_ToError()
    {
        var existing = CreateState("permission", "permission_prompt");

        var result = TeammateGate.ApplyTeammateEvent(existing, "PostToolUseFailure", null);

        result.Status.Should().Be("error");
        result.NotificationType.Should().BeEmpty();
    }

    [Fact]
    public void ApplyTeammateEvent_PermissionDenied_ClearsPermission_ToIdle()
    {
        var existing = CreateState("permission", "permission_prompt");

        var result = TeammateGate.ApplyTeammateEvent(existing, "PermissionDenied", null);

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

        var result = TeammateGate.ApplyTeammateEvent(existing, "PostToolUse", null);

        result.Status.Should().Be(existingStatus);
    }

    [Fact]
    public void ApplyTeammateEvent_Permission_NonResolutionEvent_PreservesPermission()
    {
        var existing = CreateState("permission", "permission_prompt");

        var result = TeammateGate.ApplyTeammateEvent(existing, "PreToolUse", null);

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

        var result = TeammateGate.ApplyTeammateEvent(existing, eventName, null);

        result.Status.Should().Be(status);
    }

    [Fact]
    public void ApplyTeammateEvent_IdleLead_SubagentToolUse_StaysIdle()
    {
        var existing = CreateState("idle") with { NotificationType = "idle_prompt" };

        var result = TeammateGate.ApplyTeammateEvent(existing, "PreToolUse", null);

        result.Status.Should().Be("idle");
        result.NotificationType.Should().Be("idle_prompt");
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
        };

        var result = TeammateGate.ApplyTeammateEvent(existing, "PreToolUse", null);

        result.Timestamp.Should().BeAfter(oldTime);
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

        var result = TeammateGate.ApplyTeammateEvent(existing, "PreToolUse", null);

        result.SoundPack.Should().Be("Portal Turret");
        result.IconStyle.Should().Be("hexagons");
        result.DesktopIndex.Should().Be(2);
        result.LastMessage.Should().Be("hello");
        result.SessionId.Should().Be("test-session");
    }

    // --- ApplyTeammateEvent: running-task roster ---

    [Fact]
    public void ApplyTeammateEvent_NonEmptyRoster_StoresIt()
    {
        // Payloads drawn verbatim from scratch/agent-liveness-roster/evidence/capture.log
        // (2026-08-20 13:20:38.976 SubagentStop entry). `command` is intentionally
        // unmodelled on BackgroundTaskModel per spec §4.1.
        var existing = CreateState("busy") with { RunningTasks = null };
        var roster = new List<BackgroundTaskModel>
        {
            new()
            {
                Id = "bk44y8t1j",
                Type = "shell",
                Status = "running",
                Description = "find / -ipath \"*Microsoft.AspNetCore.Antiforgery*\" -iname \"*.dll\" 2>/dev/null | head -20",
            },
            new()
            {
                Id = "a10105756c8021221",
                Type = "subagent",
                Status = "running",
                Description = "Extend antiforgery fix in spec.md",
                AgentType = "general-purpose",
            },
        };

        var result = TeammateGate.ApplyTeammateEvent(existing, "SubagentStop", roster);

        result.RunningTasks.Should().HaveCount(2);
        result.RunningTasks![0].Id.Should().Be("bk44y8t1j");
        result.RunningTasks[0].Type.Should().Be("shell");
        result.RunningTasks[0].Status.Should().Be("running");
        result.RunningTasks[0].Description.Should().Be("find / -ipath \"*Microsoft.AspNetCore.Antiforgery*\" -iname \"*.dll\" 2>/dev/null | head -20");
        result.RunningTasks[0].AgentType.Should().BeNull();
        result.RunningTasks[1].Id.Should().Be("a10105756c8021221");
        result.RunningTasks[1].Type.Should().Be("subagent");
        result.RunningTasks[1].Status.Should().Be("running");
        result.RunningTasks[1].Description.Should().Be("Extend antiforgery fix in spec.md");
        result.RunningTasks[1].AgentType.Should().Be("general-purpose");
    }

    [Fact]
    public void ApplyTeammateEvent_EmptyRoster_OverwritesExisting()
    {
        // Gate-level twin of PreserveFields_RunningTasks_EmptyListOverwritesExisting (Step 3):
        // an empty roster means "measured: nothing is running" and must overwrite a prior
        // non-empty roster, not be normalised to null and fall back to `existing` via `??`.
        var existingTasks = new List<BackgroundTaskModel>
        {
            new() { Id = "ac49354784c62a78e", Type = "subagent", Status = "running", Description = "Backfill three scope exclusions to idea.md", AgentType = "general-purpose" },
        };
        var existing = CreateState("busy") with { RunningTasks = existingTasks };

        var result = TeammateGate.ApplyTeammateEvent(existing, "SubagentStop", []);

        result.RunningTasks.Should().NotBeNull();
        result.RunningTasks.Should().BeEmpty();
    }

    [Fact]
    public void ApplyTeammateEvent_NullRoster_LeavesExistingUntouched()
    {
        // Unit-level half of spec §8 E8 (background_tasks absent on SubagentStop preserves the
        // roster). The end-to-end owner of E8 is Step 5's
        // Run_SubagentStop_WithAbsentRoster_PreservesPriorRoster.
        var existingTasks = new List<BackgroundTaskModel>
        {
            new() { Id = "a81d9ab9277c7fdbb", Type = "subagent", Status = "running", Description = "Iteration-8 plan fix pass", AgentType = "general-purpose" },
        };
        var existing = CreateState("busy") with { RunningTasks = existingTasks };

        var result = TeammateGate.ApplyTeammateEvent(existing, "SubagentStop", null);

        result.RunningTasks.Should().BeSameAs(existingTasks);
    }

    [Fact]
    public void ApplyTeammateEvent_RosterDoesNotAffectPermissionClearing()
    {
        // Regression guard: the roster is orthogonal to lead-status gating. A PostToolUse on a
        // permission lead must still clear to "busy" regardless of what the roster carries —
        // asserted across a non-empty roster, an empty roster, and null.
        var nonEmptyRoster = new List<BackgroundTaskModel>
        {
            new() { Id = "ac49354784c62a78e", Type = "subagent", Status = "running", Description = "Backfill three scope exclusions to idea.md", AgentType = "general-purpose" },
        };

        var nonEmptyResult = TeammateGate.ApplyTeammateEvent(CreateState("permission", "permission_prompt"), "PostToolUse", nonEmptyRoster);
        var emptyResult = TeammateGate.ApplyTeammateEvent(CreateState("permission", "permission_prompt"), "PostToolUse", []);
        var nullResult = TeammateGate.ApplyTeammateEvent(CreateState("permission", "permission_prompt"), "PostToolUse", null);

        nonEmptyResult.Status.Should().Be("busy");
        nonEmptyResult.NotificationType.Should().BeEmpty();
        emptyResult.Status.Should().Be("busy");
        emptyResult.NotificationType.Should().BeEmpty();
        nullResult.Status.Should().Be("busy");
        nullResult.NotificationType.Should().BeEmpty();
    }
}
