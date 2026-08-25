using System.Text.Json;
using FluentAssertions;
using Imrdy.Integration.Tests.Helpers;
using Xunit;

namespace Imrdy.Integration.Tests;

/// <summary>
/// Published-binary serialization seal for spec §4.4 (D17). Proves that a <c>background_tasks</c>
/// roster survives source-generated JSON serialization through the <b>published single-file
/// self-contained binary</b>, where a missing or wrong <c>[JsonSerializable]</c> registration
/// produces silent null output rather than a build error (see
/// <c>.claude/skills/imrdy-expert/displayitem-source-gen-gotcha.md</c>). This test deliberately
/// does <b>not</b> re-test branch logic, the D6 degradation, or the D3 self-inclusion lock — those
/// live in <c>tests/Imrdy.Core.Tests/Hooks/HookCommandTests.cs</c> (D16) and run in-process against
/// the standard unit pass. It requires a Release publish of <c>src/Imrdy.Windows</c>
/// (<c>dotnet publish src/Imrdy.Windows -c Release -r win-x64 --self-contained</c>) before it can
/// run — <see cref="CliTestFixture"/> throws if the published binary is missing.
/// </summary>
[Trait("Category", "Integration")]
public class HookCommandRunningTasksTests : IDisposable
{
    private readonly CliTestFixture _cli = new();
    private readonly TempDirectoryFixture _temp = new();
    private readonly string _sessionId = $"inttest-{Guid.NewGuid():N}"[..32];

    private Dictionary<string, string> EnvWithHome() => new()
    {
        ["IMRDY_HOME"] = _temp.Path,
    };

    private string SessionFilePath => Path.Combine(
        _temp.Path, "sessions", $"{_sessionId}.json");

    // Copied byte-for-byte from scratch/agent-liveness-roster/evidence/capture.log — the lead
    // Stop at 13:21:35.531 (spec §4.1, D4 "Apply: R-evidence"). NOT lifted from
    // evidence/roster-timeline.json, whose five-key distillation materialises an
    // "agent_type": null on the shell entry (a shape no build ever emits) and drops "command"
    // entirely — the one unmodelled key this seal exists to prove is tolerated. The shell entry
    // carries "command" and no "agent_type" key at all; the subagent entry carries "agent_type"
    // and no "command" key at all. Do not make the two shapes uniform.
    private const string RosterMixed =
        """[{"id": "bk44y8t1j", "type": "shell", "status": "running", "description": "find / -ipath \"*Microsoft.AspNetCore.Antiforgery*\" -iname \"*.dll\" 2>/dev/null | head -20", "command": "find / -ipath \"*Microsoft.AspNetCore.Antiforgery*\" -iname \"*.dll\" 2>/dev/null | head -20"}, {"id": "ad77252957e7e9c48", "type": "subagent", "status": "running", "description": "Decision traceability iter-3", "agent_type": "decision-traceability-reviewer"}]""";

    [Fact]
    public async Task Hook_Stop_WithNonEmptyRoster_SurvivesPublishedBinarySerialization()
    {
        var hookJson = "{" + string.Join(",", new[]
        {
            $"\"hook_event_name\":\"Stop\"",
            $"\"session_id\":\"{_sessionId}\"",
            $"\"cwd\":\"/d/dev/test\"",
            $"\"background_tasks\":{RosterMixed}",
        }) + "}";

        var (exitCode, _, stderr) = await _cli.RunAsync("hook", stdin: hookJson,
            workingDirectory: _temp.Path, environmentVariables: EnvWithHome());

        exitCode.Should().Be(0, $"hook should exit 0. stderr: {stderr}");
        File.Exists(SessionFilePath).Should().BeTrue("hook should write a state file");

        var content = await File.ReadAllTextAsync(SessionFilePath);
        var doc = JsonDocument.Parse(content);

        doc.RootElement.TryGetProperty("running_tasks", out var runningTasks)
            .Should().BeTrue("running_tasks should be present in the state file");
        runningTasks.ValueKind.Should().Be(JsonValueKind.Array,
            "running_tasks should be an array, not a null produced by a source-gen registration gap");
        runningTasks.GetArrayLength().Should().Be(2,
            "both roster entries should survive — not an array of nulls, not absent, not one entry");

        var shell = runningTasks[0];
        shell.GetProperty("id").GetString().Should().Be("bk44y8t1j");
        shell.GetProperty("type").GetString().Should().Be("shell");
        shell.GetProperty("status").GetString().Should().Be("running");
        shell.GetProperty("description").GetString().Should().Be(
            "find / -ipath \"*Microsoft.AspNetCore.Antiforgery*\" -iname \"*.dll\" 2>/dev/null | head -20");
        shell.GetProperty("agent_type").ValueKind.Should().Be(JsonValueKind.Null,
            "the shell entry has no agent_type key in the input — it must deserialize to null " +
            "from absence, not from an explicit null the payload never sends");

        var subagent = runningTasks[1];
        subagent.GetProperty("id").GetString().Should().Be("ad77252957e7e9c48");
        subagent.GetProperty("type").GetString().Should().Be("subagent");
        subagent.GetProperty("status").GetString().Should().Be("running");
        subagent.GetProperty("description").GetString().Should().Be("Decision traceability iter-3");
        subagent.GetProperty("agent_type").GetString().Should().Be("decision-traceability-reviewer");
    }

    public void Dispose()
    {
        _temp.Dispose();
    }
}
