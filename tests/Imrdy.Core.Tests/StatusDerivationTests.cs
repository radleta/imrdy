using Imrdy.Core.Status;
using FluentAssertions;

namespace Imrdy.Core.Tests;

public class StatusDerivationTests
{
    [Theory]
    [InlineData("SessionStart", "start")]
    [InlineData("UserPromptSubmit", "busy")]
    [InlineData("PreToolUse", "busy")]
    [InlineData("PreCompact", "compact")]
    [InlineData("Stop", "done")]
    [InlineData("Notification", "attention")]
    [InlineData("PermissionRequest", "permission")]
    [InlineData("SessionEnd", "end")]
    [InlineData("PostToolUse", "busy")]
    [InlineData("PostToolUseFailure", "error")]
    [InlineData("StopFailure", "error")]
    [InlineData("SubagentStart", "busy")]
    [InlineData("SubagentStop", "busy")]
    [InlineData("PostCompact", "idle")]
    [InlineData("Elicitation", "permission")]
    [InlineData("WorktreeCreate", "busy")]
    [InlineData("TaskCreated", "busy")]
    [InlineData("TaskCompleted", "busy")]
    [InlineData("TeammateIdle", "busy")]
    [InlineData("PermissionDenied", "idle")]
    public void DeriveStatus_StandardEvents_ReturnExpectedStatus(string eventName, string expected)
    {
        StatusDerivation.DeriveStatus(eventName).Should().Be(expected);
    }

    [Fact]
    public void DeriveStatus_SessionStartWithResume_ReturnsIdle()
    {
        StatusDerivation.DeriveStatus("SessionStart", source: "resume").Should().Be("idle");
    }

    [Fact]
    public void DeriveStatus_SessionStartWithoutResume_ReturnsStart()
    {
        StatusDerivation.DeriveStatus("SessionStart").Should().Be("start");
        StatusDerivation.DeriveStatus("SessionStart", source: "new").Should().Be("start");
    }

    [Fact]
    public void DeriveStatus_NotificationWithPermissionPrompt_ReturnsPermission()
    {
        StatusDerivation.DeriveStatus("Notification", notificationType: "permission_prompt")
            .Should().Be("permission");
    }

    [Fact]
    public void DeriveStatus_NotificationWithIdlePrompt_ReturnsIdle()
    {
        StatusDerivation.DeriveStatus("Notification", notificationType: "idle_prompt")
            .Should().Be("idle");
    }

    [Fact]
    public void DeriveStatus_NotificationWithOtherType_ReturnsAttention()
    {
        StatusDerivation.DeriveStatus("Notification", notificationType: "task_notification")
            .Should().Be("attention");
    }

    [Fact]
    public void DeriveStatus_UnknownEvent_ReturnsUnknown()
    {
        StatusDerivation.DeriveStatus("SomeNewEvent").Should().Be("unknown");
    }

    [Fact]
    public void DeriveStatus_CaseInsensitive()
    {
        StatusDerivation.DeriveStatus("sessionstart", source: "RESUME").Should().Be("idle");
        StatusDerivation.DeriveStatus("notification", notificationType: "PERMISSION_PROMPT").Should().Be("permission");
        StatusDerivation.DeriveStatus("notification", notificationType: "IDLE_PROMPT").Should().Be("idle");
    }

    [Fact]
    public void DeriveStatus_NullSource_DoesNotThrow()
    {
        StatusDerivation.DeriveStatus("SessionStart", source: null).Should().Be("start");
    }

    [Fact]
    public void DeriveStatus_NullNotificationType_DoesNotThrow()
    {
        StatusDerivation.DeriveStatus("Notification", notificationType: null).Should().Be("attention");
    }

    // Decision table coverage — Step 04 (rows 1-5 plus event-name path)

    [Fact]
    public void DeriveStatus_NotificationWithPermissionPrompt_ReturnsPermission_Regression()
    {
        // Decision table row 1 — regression-prevention for existing path
        StatusDerivation.DeriveStatus("Notification", notificationType: "permission_prompt")
            .Should().Be("permission");
    }

    [Fact]
    public void DeriveStatus_NotificationWithElicitationDialog_ReturnsPermission()
    {
        // Decision table row 3 — new mapping: elicitation_dialog subtype → permission
        StatusDerivation.DeriveStatus("Notification", notificationType: "elicitation_dialog")
            .Should().Be("permission");
    }

    [Fact]
    public void DeriveStatus_NotificationWithElicitationDialogMixedCase_ReturnsPermission()
    {
        // Decision table row 3 — case-insensitivity guard: protects against switch-expression regression
        StatusDerivation.DeriveStatus("Notification", notificationType: "Elicitation_Dialog")
            .Should().Be("permission");
    }

    [Fact]
    public void DeriveStatus_NotificationWithIdlePrompt_ReturnsIdle_Regression()
    {
        // Decision table row 2 — regression-prevention for existing path
        StatusDerivation.DeriveStatus("Notification", notificationType: "idle_prompt")
            .Should().Be("idle");
    }

    [Fact]
    public void DeriveStatus_NotificationWithUnrecognizedType_ReturnsAttention_Regression()
    {
        // Decision table row 4 — default fallback for any other notification type
        StatusDerivation.DeriveStatus("Notification", notificationType: "some_other_type")
            .Should().Be("attention");
    }

    [Fact]
    public void DeriveStatus_ElicitationEventName_ReturnsPermission_Regression()
    {
        // Decision table row 5 — event-name path (orthogonal to Notification subtype path)
        StatusDerivation.DeriveStatus("Elicitation")
            .Should().Be("permission");
    }
}
