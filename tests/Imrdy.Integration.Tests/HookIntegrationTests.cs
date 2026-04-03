using System.Text.Json;
using FluentAssertions;
using Imrdy.Integration.Tests.Helpers;
using Xunit;

namespace Imrdy.Integration.Tests;

public class HookIntegrationTests : IDisposable
{
    private readonly CliTestFixture _cli = new();
    private readonly TempDirectoryFixture _temp = new();
    private readonly string _sessionId = $"inttest-{Guid.NewGuid():N}"[..32];
    private readonly List<string> _createdStateFiles = new();

    private string SessionFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".imrdy", "sessions", $"{_sessionId}.json");

    [Fact]
    public async Task Hook_StopEvent_ExitsZero()
    {
        var hookJson = JsonSerializer.Serialize(new
        {
            session_id = _sessionId,
            hook_event_name = "Stop",
            cwd = "/d/dev/test",
        });

        var (exitCode, _, _) = await _cli.RunAsync("hook", stdin: hookJson, workingDirectory: _temp.Path);
        _createdStateFiles.Add(SessionFilePath);

        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task Hook_StopEvent_WritesStateFile()
    {
        var hookJson = JsonSerializer.Serialize(new
        {
            session_id = _sessionId,
            hook_event_name = "Stop",
            cwd = "/d/dev/test",
        });

        await _cli.RunAsync("hook", stdin: hookJson, workingDirectory: _temp.Path);
        _createdStateFiles.Add(SessionFilePath);

        File.Exists(SessionFilePath).Should().BeTrue("hook should write a state file");

        var content = await File.ReadAllTextAsync(SessionFilePath);
        var doc = JsonDocument.Parse(content);
        doc.RootElement.GetProperty("session_id").GetString().Should().Be(_sessionId);
    }

    [Fact]
    public async Task Hook_ThenStatus_SessionAppearsInOutput()
    {
        var hookJson = JsonSerializer.Serialize(new
        {
            session_id = _sessionId,
            hook_event_name = "Stop",
            cwd = "/d/dev/test",
        });

        await _cli.RunAsync("hook", stdin: hookJson, workingDirectory: _temp.Path);
        _createdStateFiles.Add(SessionFilePath);

        var (exitCode, stdout, _) = await _cli.RunAsync("status --json", workingDirectory: _temp.Path);

        exitCode.Should().Be(0);
        stdout.Should().Contain(_sessionId);
    }

    [Fact]
    public async Task Hook_LifecycleSequence_ReflectsLatestState()
    {
        // First: Stop event
        var stop1 = JsonSerializer.Serialize(new
        {
            session_id = _sessionId,
            hook_event_name = "Stop",
            cwd = "/d/dev/test",
            notification_type = "user",
        });
        await _cli.RunAsync("hook", stdin: stop1, workingDirectory: _temp.Path);
        _createdStateFiles.Add(SessionFilePath);

        // Second: another Stop with different notification_type
        var stop2 = JsonSerializer.Serialize(new
        {
            session_id = _sessionId,
            hook_event_name = "Stop",
            cwd = "/d/dev/test",
            notification_type = "assistant",
        });
        await _cli.RunAsync("hook", stdin: stop2, workingDirectory: _temp.Path);

        // Third: Start event
        var start = JsonSerializer.Serialize(new
        {
            session_id = _sessionId,
            hook_event_name = "Start",
            cwd = "/d/dev/test",
        });
        await _cli.RunAsync("hook", stdin: start, workingDirectory: _temp.Path);

        // Verify state reflects the latest event
        var content = await File.ReadAllTextAsync(SessionFilePath);
        var doc = JsonDocument.Parse(content);
        doc.RootElement.GetProperty("hook_event").GetString().Should().Be("Start");
    }

    public void Dispose()
    {
        foreach (var path in _createdStateFiles)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Best-effort cleanup
            }
        }

        _temp.Dispose();
    }
}
