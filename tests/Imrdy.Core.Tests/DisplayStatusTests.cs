using FluentAssertions;
using Imrdy.Core.Hooks;
using Imrdy.Core.Status;
using Xunit;

namespace Imrdy.Core.Tests;

public class DisplayStatusTests
{
    /// <summary>
    /// A subagent roster entry, verbatim from <c>evidence/capture.log</c> — the first subagent
    /// element of the longest observed <c>background_tasks</c> payload. Subagent entries carry
    /// <c>agent_type</c> and no <c>command</c>.
    /// </summary>
    private static BackgroundTaskModel SubagentEntry() => new()
    {
        Id = "a99b6c8e69d659066",
        Type = "subagent",
        Status = "running",
        Description = "Codebase alignment confirmation iter-4",
        AgentType = "codebase-alignment-reviewer",
    };

    // spec §3 C2 — a stale roster is unreachable while it matters: a lead that is not "idle"
    // ignores the roster entirely, so every non-idle status passes through even when work is
    // running. "done" is not a stored status at all; it is Resolve's own output, included here as
    // a defensive idempotence case.
    [Theory]
    [InlineData("busy")]
    [InlineData("error")]
    [InlineData("permission")]
    [InlineData("attention")]
    [InlineData("compact")]
    [InlineData("start")]
    [InlineData("end")]
    [InlineData("unknown")]
    [InlineData("done")]
    public void Resolve_NonIdleStatus_PassesThroughEvenWithNonEmptyRoster(string status)
    {
        DisplayStatus.Resolve(status, [SubagentEntry()]).Should().Be(status);
    }

    [Fact]
    public void Resolve_IdleWithNullRoster_ShowsGreen()
    {
        // No measurement at all (D6 degradation): a Claude Code build that stops sending
        // background_tasks returns imrdy to lead-readiness-only behaviour rather than stranding it.
        DisplayStatus.Resolve("idle", null).Should().Be("idle");
    }

    [Fact]
    public void Resolve_IdleWithEmptyRoster_ShowsGreen()
    {
        // Measured empty — a different meaning from null, which is why it is a separate test:
        // work was reported on, and none of it is still running.
        DisplayStatus.Resolve("idle", []).Should().Be("idle");
    }

    [Fact]
    public void Resolve_IdleWithNonEmptyRoster_ShowsTeal()
    {
        DisplayStatus.Resolve("idle", [SubagentEntry()]).Should().Be("done");
    }

    [Fact]
    public void Resolve_IdleWithMultiEntryRoster_ShowsTeal()
    {
        // Pins the predicate as Count > 0, not Count == 1. Second entry is verbatim from
        // evidence/capture.log, adjacent to SubagentEntry() in the same roster.
        var second = new BackgroundTaskModel
        {
            Id = "a4ed4f684dbc92039",
            Type = "subagent",
            Status = "running",
            Description = "Combinatorial completeness iter-4",
            AgentType = "combinatorial-completeness-reviewer",
        };

        DisplayStatus.Resolve("idle", [SubagentEntry(), second]).Should().Be("done");
    }

    [Fact]
    public void Resolve_IdleWithShellOnlyRoster_ShowsTeal()
    {
        // spec §8 E2 — a lead-backgrounded shell with no subagents at all. The roster must not be
        // filtered to subagent entries: this is the case the retired marker-file design could not
        // see. Verbatim from evidence/capture.log; `command` is unmodelled per spec §4.1, and
        // shell entries carry no agent_type.
        var shell = new BackgroundTaskModel
        {
            Id = "bk44y8t1j",
            Type = "shell",
            Status = "running",
            Description = "find / -ipath \"*Microsoft.AspNetCore.Antiforgery*\" -iname \"*.dll\" 2>/dev/null | head -20",
            AgentType = null,
        };

        DisplayStatus.Resolve("idle", [shell]).Should().Be("done");
    }

    [Fact]
    public void Resolve_IsCaseInsensitiveOnStatus()
    {
        DisplayStatus.Resolve("IDLE", [SubagentEntry()]).Should().Be("done");
    }

    [Fact]
    public void IsIdleWithAgentsRunning_TrueOnlyForIdleLeadWithLiveAgents()
    {
        DisplayStatus.IsIdleWithAgentsRunning("idle", [SubagentEntry()]).Should().BeTrue();
        DisplayStatus.IsIdleWithAgentsRunning("idle", []).Should().BeFalse();
        DisplayStatus.IsIdleWithAgentsRunning("idle", null).Should().BeFalse();
        DisplayStatus.IsIdleWithAgentsRunning("busy", [SubagentEntry()]).Should().BeFalse();
    }
}
