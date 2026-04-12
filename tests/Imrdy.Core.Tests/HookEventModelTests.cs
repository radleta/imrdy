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
