using System.Text.Json;
using FluentAssertions;
using Imrdy.Core;
using Imrdy.Core.Hooks;

namespace Imrdy.Core.Tests.Hooks;

public class BackgroundTaskModelTests
{
    // Verbatim from scratch/agent-liveness-roster/evidence/capture.log — the lead `Stop` at
    // 13:21:35.531 (Apply: R-evidence). Do not author or distill this shape.
    private const string MixedRosterJson = """
        [{"id": "bk44y8t1j", "type": "shell", "status": "running", "description": "find / -ipath \"*Microsoft.AspNetCore.Antiforgery*\" -iname \"*.dll\" 2>/dev/null | head -20", "command": "find / -ipath \"*Microsoft.AspNetCore.Antiforgery*\" -iname \"*.dll\" 2>/dev/null | head -20"},
         {"id": "ad77252957e7e9c48", "type": "subagent", "status": "running", "description": "Decision traceability iter-3", "agent_type": "decision-traceability-reviewer"}]
        """;

    [Fact]
    public void BackgroundTaskModel_MixedRoster_RoundTripsAllProperties()
    {
        var roster = JsonSerializer.Deserialize(MixedRosterJson, ImrdyJsonContext.Default.ListBackgroundTaskModel);

        roster.Should().NotBeNull();
        roster.Should().HaveCount(2);

        var shellEntry = roster![0];
        shellEntry.Id.Should().Be("bk44y8t1j");
        shellEntry.Type.Should().Be("shell");
        shellEntry.Status.Should().Be("running");
        shellEntry.Description.Should().Be("find / -ipath \"*Microsoft.AspNetCore.Antiforgery*\" -iname \"*.dll\" 2>/dev/null | head -20");
        // The shell entry has no "agent_type" key at all — an absent key deserializes to null,
        // never a present-as-null value. See spec §4.1.
        shellEntry.AgentType.Should().BeNull();

        var subagentEntry = roster[1];
        subagentEntry.Id.Should().Be("ad77252957e7e9c48");
        subagentEntry.Type.Should().Be("subagent");
        subagentEntry.Status.Should().Be("running");
        subagentEntry.Description.Should().Be("Decision traceability iter-3");
        subagentEntry.AgentType.Should().Be("decision-traceability-reviewer");
    }

    [Fact]
    public void BackgroundTaskModel_Serialize_EmitsSnakeCaseKeys()
    {
        var entry = new BackgroundTaskModel
        {
            Id = "ad77252957e7e9c48",
            Type = "subagent",
            Status = "running",
            Description = "Decision traceability iter-3",
            AgentType = "decision-traceability-reviewer",
        };

        var json = JsonSerializer.Serialize(entry, ImrdyJsonContext.Default.BackgroundTaskModel);

        json.Should().Contain("\"id\"");
        json.Should().Contain("\"type\"");
        json.Should().Contain("\"status\"");
        json.Should().Contain("\"description\"");
        json.Should().Contain("\"agent_type\"");
    }

    [Fact]
    public void BackgroundTaskModel_UnknownMember_CommandIsDroppedNotThrown()
    {
        // "command" is the real unknown member — present on every type:"shell" entry (61/61 in
        // the corpus) and deliberately unmodelled per spec §4.1. No [JsonExtensionData] member
        // exists on BackgroundTaskModel, so it must be silently dropped, not throw.
        var json = """
            {"id": "bk44y8t1j", "type": "shell", "status": "running", "description": "find / -ipath \"*Microsoft.AspNetCore.Antiforgery*\" -iname \"*.dll\" 2>/dev/null | head -20", "command": "find / -ipath \"*Microsoft.AspNetCore.Antiforgery*\" -iname \"*.dll\" 2>/dev/null | head -20"}
            """;

        var act = () => JsonSerializer.Deserialize(json, ImrdyJsonContext.Default.BackgroundTaskModel);

        var entry = act.Should().NotThrow().Subject;
        entry.Should().NotBeNull();
        entry!.Id.Should().Be("bk44y8t1j");
        entry.Type.Should().Be("shell");

        // "command" has no corresponding property, so it cannot appear anywhere on the record.
        var roundTripped = JsonSerializer.Serialize(entry, ImrdyJsonContext.Default.BackgroundTaskModel);
        roundTripped.Should().NotContain("command");
    }
}
