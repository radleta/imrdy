using System.Text.Json;
using FluentAssertions;
using Imrdy.Integration.Tests.Helpers;
using Xunit;

namespace Imrdy.Integration.Tests;

/// <summary>
/// Integration test verifying that HookCommand populates <c>started_at</c> in the state file
/// when the hook event is <c>SessionStart</c> and no prior state file exists (first-ever start).
/// </summary>
[Trait("Category", "Integration")]
public class HookCommandStartedAtTests : IDisposable
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

    [Fact]
    public async Task Hook_SessionStart_PopulatesStartedAt()
    {
        var before = DateTimeOffset.UtcNow;

        var hookJson = JsonSerializer.Serialize(new
        {
            session_id = _sessionId,
            hook_event_name = "SessionStart",
            cwd = "/d/dev/test",
        });

        var (exitCode, _, stderr) = await _cli.RunAsync("hook", stdin: hookJson,
            workingDirectory: _temp.Path, environmentVariables: EnvWithHome());

        exitCode.Should().Be(0, $"hook should exit 0. stderr: {stderr}");
        File.Exists(SessionFilePath).Should().BeTrue("hook should write a state file");

        var content = await File.ReadAllTextAsync(SessionFilePath);
        var doc = JsonDocument.Parse(content);

        doc.RootElement.TryGetProperty("started_at", out var startedAtElement)
            .Should().BeTrue("started_at should be present in the state file");

        var startedAtStr = startedAtElement.GetString();
        startedAtStr.Should().NotBeNullOrEmpty("started_at should have a value");

        var startedAt = DateTimeOffset.Parse(startedAtStr!);
        var after = DateTimeOffset.UtcNow;

        startedAt.Should().BeOnOrAfter(before.AddSeconds(-1),
            "started_at should be close to the hook execution time");
        startedAt.Should().BeOnOrBefore(after.AddSeconds(5),
            "started_at should not be in the future");
    }

    [Fact]
    public async Task Hook_SessionStart_PreservesStartedAtOnSubsequentEvents()
    {
        // First: SessionStart — sets started_at
        var start = JsonSerializer.Serialize(new
        {
            session_id = _sessionId,
            hook_event_name = "SessionStart",
            cwd = "/d/dev/test",
        });

        await _cli.RunAsync("hook", stdin: start,
            workingDirectory: _temp.Path, environmentVariables: EnvWithHome());

        var content1 = await File.ReadAllTextAsync(SessionFilePath);
        var firstStartedAt = JsonDocument.Parse(content1).RootElement
            .GetProperty("started_at").GetString();

        // Small delay to ensure second event has a later timestamp
        await Task.Delay(50);

        // Second: PreToolUse — should preserve started_at from first event
        var preToolUse = JsonSerializer.Serialize(new
        {
            session_id = _sessionId,
            hook_event_name = "PreToolUse",
            cwd = "/d/dev/test",
            tool_name = "Bash",
        });

        await _cli.RunAsync("hook", stdin: preToolUse,
            workingDirectory: _temp.Path, environmentVariables: EnvWithHome());

        var content2 = await File.ReadAllTextAsync(SessionFilePath);
        var secondStartedAt = JsonDocument.Parse(content2).RootElement
            .GetProperty("started_at").GetString();

        secondStartedAt.Should().Be(firstStartedAt,
            "started_at should be preserved across subsequent hook events via FieldPreservation");
    }

    [Fact]
    public async Task Hook_SecondSessionStart_PreservesOriginalStartedAt()
    {
        // First: SessionStart — sets started_at
        var start1 = JsonSerializer.Serialize(new
        {
            session_id = _sessionId,
            hook_event_name = "SessionStart",
            cwd = "/d/dev/test",
        });

        await _cli.RunAsync("hook", stdin: start1,
            workingDirectory: _temp.Path, environmentVariables: EnvWithHome());

        var content1 = await File.ReadAllTextAsync(SessionFilePath);
        var originalStartedAt = JsonDocument.Parse(content1).RootElement
            .GetProperty("started_at").GetString();

        // Small delay to ensure second SessionStart would produce a different timestamp
        await Task.Delay(50);

        // Second: another SessionStart (reconnect or tray restart) — must NOT overwrite started_at
        var start2 = JsonSerializer.Serialize(new
        {
            session_id = _sessionId,
            hook_event_name = "SessionStart",
            cwd = "/d/dev/test",
        });

        await _cli.RunAsync("hook", stdin: start2,
            workingDirectory: _temp.Path, environmentVariables: EnvWithHome());

        var content2 = await File.ReadAllTextAsync(SessionFilePath);
        var secondStartedAt = JsonDocument.Parse(content2).RootElement
            .GetProperty("started_at").GetString();

        secondStartedAt.Should().Be(originalStartedAt,
            "a second SessionStart must not overwrite the original started_at");
    }

    public void Dispose()
    {
        _temp.Dispose();
    }
}
