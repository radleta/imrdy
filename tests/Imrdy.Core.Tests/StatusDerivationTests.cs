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
    [InlineData("Stop", "idle")]
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
    public void DeriveStatus_NotificationWithOtherType_ReturnsAttention()
    {
        StatusDerivation.DeriveStatus("Notification", notificationType: "idle_prompt")
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
}
