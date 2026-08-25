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

    // Verbatim from scratch/agent-liveness-roster/evidence/capture.log — the `SubagentStop` at
    // 13:20:38.977 (Apply: R-evidence). Do not author or distill this shape; the shell entry's
    // absent "agent_type" key and present "command" key are exactly what this test exercises.
    [Fact]
    public void HookEventModel_BackgroundTasks_DeserializesMixedRoster()
    {
        var json = """
        {
            "hook_event_name": "SubagentStop",
            "session_id": "test-123",
            "cwd": "/tmp",
            "background_tasks": [
                {"id": "bk44y8t1j", "type": "shell", "status": "running", "description": "find / -ipath \"*Microsoft.AspNetCore.Antiforgery*\" -iname \"*.dll\" 2>/dev/null | head -20", "command": "find / -ipath \"*Microsoft.AspNetCore.Antiforgery*\" -iname \"*.dll\" 2>/dev/null | head -20"},
                {"id": "ac49354784c62a78e", "type": "subagent", "status": "running", "description": "Backfill three scope exclusions to idea.md", "agent_type": "general-purpose"}
            ]
        }
        """;
        var model = JsonSerializer.Deserialize(json, ImrdyJsonContext.Default.HookEventModel);
        model.Should().NotBeNull();
        model!.BackgroundTasks.Should().NotBeNull();
        model.BackgroundTasks.Should().HaveCount(2);

        var shellEntry = model.BackgroundTasks![0];
        shellEntry.Id.Should().Be("bk44y8t1j");
        shellEntry.Type.Should().Be("shell");
        shellEntry.Status.Should().Be("running");
        shellEntry.Description.Should().Be("find / -ipath \"*Microsoft.AspNetCore.Antiforgery*\" -iname \"*.dll\" 2>/dev/null | head -20");
        // Absent "agent_type" key on a shell entry deserializes to null — not a present-as-null
        // value. See spec §4.1.
        shellEntry.AgentType.Should().BeNull();

        var subagentEntry = model.BackgroundTasks[1];
        subagentEntry.Id.Should().Be("ac49354784c62a78e");
        subagentEntry.Type.Should().Be("subagent");
        subagentEntry.Status.Should().Be("running");
        subagentEntry.Description.Should().Be("Backfill three scope exclusions to idea.md");
        subagentEntry.AgentType.Should().Be("general-purpose");
    }

    [Fact]
    public void HookEventModel_BackgroundTasks_NullWhenAbsent()
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
        model!.BackgroundTasks.Should().BeNull();
    }

    [Fact]
    public void HookEventModel_BackgroundTasks_EmptyArrayIsNotNull()
    {
        var json = """
        {
            "hook_event_name": "Stop",
            "session_id": "test-123",
            "cwd": "/tmp",
            "background_tasks": []
        }
        """;
        var model = JsonSerializer.Deserialize(json, ImrdyJsonContext.Default.HookEventModel);
        model.Should().NotBeNull();
        // Present-and-empty is measured "nothing running" — distinct from absent ("no
        // information"). Collapsing the two would strand a session at teal. See spec §4.2, §6.2.
        model!.BackgroundTasks.Should().NotBeNull();
        model.BackgroundTasks!.Should().BeEmpty();
    }

    [Fact]
    public void HookEventModel_BackgroundTasks_NotInExtensionData()
    {
        var json = """
        {
            "hook_event_name": "SubagentStop",
            "session_id": "test-123",
            "cwd": "/tmp",
            "background_tasks": [
                {"id": "bk44y8t1j", "type": "shell", "status": "running", "description": "find / -ipath \"*Microsoft.AspNetCore.Antiforgery*\" -iname \"*.dll\" 2>/dev/null | head -20", "command": "find / -ipath \"*Microsoft.AspNetCore.Antiforgery*\" -iname \"*.dll\" 2>/dev/null | head -20"},
                {"id": "ac49354784c62a78e", "type": "subagent", "status": "running", "description": "Backfill three scope exclusions to idea.md", "agent_type": "general-purpose"}
            ]
        }
        """;
        var model = JsonSerializer.Deserialize(json, ImrdyJsonContext.Default.HookEventModel);
        model.Should().NotBeNull();
        model!.BackgroundTasks.Should().NotBeNull();
        // Typed property claims the key — it must not also land in ExtensionData. Documents the
        // Step 5 log-line obligation in executable form (background_tasks no longer prints via
        // the ExtensionData loop after this step).
        if (model!.ExtensionData is not null)
        {
            model.ExtensionData.Should().NotContainKey("background_tasks");
        }
    }
}
