using System.Text.Json;
using FluentAssertions;
using Imrdy.Core;
using Imrdy.Core.Hooks;

namespace Imrdy.Core.Tests;

public class HookEventModelTests
{
    [Fact]
    public void HookEventModel_ExtensionData_CapturesUnknownFields()
    {
        var json = """
        {
            "hook_event_name": "PreToolUse",
            "session_id": "test-123",
            "cwd": "/tmp",
            "unknown_string": "value",
            "unknown_nested": { "key": "nested_value" }
        }
        """;
        var model = JsonSerializer.Deserialize(json, ImrdyJsonContext.Default.HookEventModel);
        model.Should().NotBeNull();
        model!.ExtensionData.Should().NotBeNull();
        model.ExtensionData.Should().ContainKey("unknown_string");
        model.ExtensionData.Should().ContainKey("unknown_nested");
        model.ExtensionData!["unknown_nested"].ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public void HookEventModel_AgentId_Deserializes()
    {
        var json = """
        {
            "hook_event_name": "PreToolUse",
            "session_id": "test-123",
            "cwd": "/tmp",
            "agent_id": "aa4f17a21692e67b0",
            "agent_type": "worker"
        }
        """;
        var model = JsonSerializer.Deserialize(json, ImrdyJsonContext.Default.HookEventModel);
        model.Should().NotBeNull();
        model!.AgentId.Should().Be("aa4f17a21692e67b0");
        model.AgentType.Should().Be("worker");
    }

    [Fact]
    public void HookEventModel_AgentId_NullWhenAbsent()
    {
        var json = """
        {
            "hook_event_name": "Stop",
            "session_id": "test-123",
            "cwd": "/tmp"
        }
        """;
        var model = JsonSerializer.Deserialize(json, ImrdyJsonContext.Default.HookEventModel);
        model.Should().NotBeNull();
        model!.AgentId.Should().BeNull();
        model.AgentType.Should().BeNull();
    }

    [Fact]
    public void HookEventModel_AgentId_NotInExtensionData()
    {
        var json = """
        {
            "hook_event_name": "PreToolUse",
            "session_id": "test-123",
            "cwd": "/tmp",
            "agent_id": "aa4f17a21692e67b0",
            "agent_type": "worker"
        }
        """;
        var model = JsonSerializer.Deserialize(json, ImrdyJsonContext.Default.HookEventModel);
        model.Should().NotBeNull();
        // Typed properties should NOT appear in ExtensionData
        model!.ExtensionData.Should().BeNull();
    }

    [Fact]
    public void HookEventModel_ToolName_Deserializes()
    {
        var json = """
        {
            "hook_event_name": "PreToolUse",
            "session_id": "test-123",
            "cwd": "/tmp",
            "tool_name": "Read"
        }
        """;
        var model = JsonSerializer.Deserialize(json, ImrdyJsonContext.Default.HookEventModel);
        model.Should().NotBeNull();
        model!.ToolName.Should().Be("Read");
    }
}
