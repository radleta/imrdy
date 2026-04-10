using FluentAssertions;
using Imrdy.Integration.Tests.Helpers;
using Xunit;

namespace Imrdy.Integration.Tests;

[Trait("Category", "Integration")]
public class ConfigIntegrationTests : IDisposable
{
    private readonly CliTestFixture _cli = new();
    private readonly TempDirectoryFixture _temp = new();

    private Dictionary<string, string> EnvWithHome() => new()
    {
        ["IMRDY_HOME"] = _temp.Path,
    };

    [Fact]
    public async Task ConfigSet_SoundEnabledFalse_ExitsZero()
    {
        var (exitCode, _, _) = await _cli.RunAsync("config set sound.enabled false",
            workingDirectory: _temp.Path, environmentVariables: EnvWithHome());

        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task ConfigSet_ThenShow_ReflectsValue()
    {
        var env = EnvWithHome();

        // Set sound.enabled to false
        var (setExit, _, _) = await _cli.RunAsync("config set sound.enabled false",
            workingDirectory: _temp.Path, environmentVariables: env);
        setExit.Should().Be(0);

        // Show config and verify
        var (showExit, stdout, _) = await _cli.RunAsync("config show",
            workingDirectory: _temp.Path, environmentVariables: env);
        showExit.Should().Be(0);
        stdout.Should().Contain("false", "config show should reflect the set value");

        // Set sound.enabled to true
        var (setExit2, _, _) = await _cli.RunAsync("config set sound.enabled true",
            workingDirectory: _temp.Path, environmentVariables: env);
        setExit2.Should().Be(0);

        // Show config again
        var (showExit2, stdout2, _) = await _cli.RunAsync("config show",
            workingDirectory: _temp.Path, environmentVariables: env);
        showExit2.Should().Be(0);
        stdout2.Should().Contain("true", "config show should reflect the updated value");
    }

    [Fact]
    public async Task ConfigSet_TrayEnabled_ExitsZero()
    {
        var (exitCode, _, _) = await _cli.RunAsync("config set tray.enabled false",
            workingDirectory: _temp.Path, environmentVariables: EnvWithHome());

        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task ConfigSet_SoundDefaultPack_ExitsZero()
    {
        var (exitCode, _, _) = await _cli.RunAsync("config set sound.defaultPack mypack",
            workingDirectory: _temp.Path, environmentVariables: EnvWithHome());

        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task ConfigSet_SoundProject_ExitsZero()
    {
        var (exitCode, _, _) = await _cli.RunAsync("config set sound.projects.myproject mypack",
            workingDirectory: _temp.Path, environmentVariables: EnvWithHome());

        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task ConfigSet_UnknownKey_ExitsOne()
    {
        var (exitCode, _, _) = await _cli.RunAsync("config set bogusKey value",
            workingDirectory: _temp.Path, environmentVariables: EnvWithHome());

        exitCode.Should().Be(1);
    }

    [Fact]
    public async Task ConfigSet_InvalidBool_ExitsOne()
    {
        var (exitCode, _, _) = await _cli.RunAsync("config set sound.enabled notabool",
            workingDirectory: _temp.Path, environmentVariables: EnvWithHome());

        exitCode.Should().Be(1);
    }

    [Fact]
    public async Task ConfigSet_OverlayEnabled_ExitsZero()
    {
        var (exitCode, _, _) = await _cli.RunAsync("config set overlay.enabled true",
            workingDirectory: _temp.Path, environmentVariables: EnvWithHome());

        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task ConfigSet_OverlayPosition_ExitsZero()
    {
        var (exitCode, _, _) = await _cli.RunAsync("config set overlay.position bottom-left",
            workingDirectory: _temp.Path, environmentVariables: EnvWithHome());

        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task ConfigSet_OverlaySize_ExitsZero()
    {
        var (exitCode, _, _) = await _cli.RunAsync("config set overlay.size 128",
            workingDirectory: _temp.Path, environmentVariables: EnvWithHome());

        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task ConfigSet_OverlayEnabled_InvalidBool_ExitsOne()
    {
        var (exitCode, _, _) = await _cli.RunAsync("config set overlay.enabled notabool",
            workingDirectory: _temp.Path, environmentVariables: EnvWithHome());

        exitCode.Should().Be(1);
    }

    public void Dispose()
    {
        _temp.Dispose();
    }
}
