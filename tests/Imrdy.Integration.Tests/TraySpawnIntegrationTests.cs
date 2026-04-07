using System.Text.Json;
using FluentAssertions;
using Imrdy.Integration.Tests.Helpers;
using Xunit;

namespace Imrdy.Integration.Tests;

public class TraySpawnIntegrationTests : IDisposable
{
    private readonly CliTestFixture _cli = new();
    private readonly TempDirectoryFixture _temp = new();
    private readonly string _sessionId = $"traytest-{Guid.NewGuid():N}"[..32];

    private Dictionary<string, string> EnvWithHome() => new()
    {
        ["IMRDY_HOME"] = _temp.Path,
    };

    [Fact]
    public async Task Hook_WithTrayEnabled_ExitsZero()
    {
        // Write config with tray enabled
        var configPath = Path.Combine(_temp.Path, "config.json");
        await File.WriteAllTextAsync(configPath, """{"tray":{"enabled":true}}""");

        var hookJson = JsonSerializer.Serialize(new
        {
            session_id = _sessionId,
            hook_event_name = "Stop",
            cwd = "/d/dev/test",
        });

        var (exitCode, _, _) = await _cli.RunAsync("hook", stdin: hookJson,
            workingDirectory: _temp.Path, environmentVariables: EnvWithHome());

        exitCode.Should().Be(0, "hook must exit 0 regardless of tray spawn outcome");
    }

    [Fact]
    public async Task Hook_WithTrayDisabled_ExitsZero()
    {
        // Write config with tray disabled
        var configPath = Path.Combine(_temp.Path, "config.json");
        await File.WriteAllTextAsync(configPath, """{"tray":{"enabled":false}}""");

        var hookJson = JsonSerializer.Serialize(new
        {
            session_id = _sessionId,
            hook_event_name = "Stop",
            cwd = "/d/dev/test",
        });

        var (exitCode, _, stderr) = await _cli.RunAsync("hook", stdin: hookJson,
            workingDirectory: _temp.Path, environmentVariables: EnvWithHome());

        exitCode.Should().Be(0);
        stderr.Should().NotContain("Tray started", "tray should not be spawned when disabled");
    }

    [Fact]
    public async Task Hook_NoConfig_ExitsZero()
    {
        // No config.json — ConfigReader.Read() returns defaults (tray.enabled = true).
        // TraySpawner may or may not succeed, but the hook must still exit 0.
        var hookJson = JsonSerializer.Serialize(new
        {
            session_id = _sessionId,
            hook_event_name = "Start",
            cwd = "/d/dev/test",
        });

        var (exitCode, _, _) = await _cli.RunAsync("hook", stdin: hookJson,
            workingDirectory: _temp.Path, environmentVariables: EnvWithHome());

        exitCode.Should().Be(0, "hook must exit 0 even with no config file (defaults apply)");
    }

    public void Dispose()
    {
        _temp.Dispose();
    }
}
