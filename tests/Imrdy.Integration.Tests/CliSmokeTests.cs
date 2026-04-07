using System.Text.Json;
using FluentAssertions;
using Imrdy.Integration.Tests.Helpers;
using Xunit;

namespace Imrdy.Integration.Tests;

public class CliSmokeTests : IDisposable
{
    private readonly CliTestFixture _cli = new();
    private readonly TempDirectoryFixture _temp = new();

    private Dictionary<string, string> EnvWithHome() => new()
    {
        ["IMRDY_HOME"] = _temp.Path,
    };

    [Fact]
    public async Task Version_ReturnsZeroAndOutputsVersionString()
    {
        var (exitCode, stdout, _) = await _cli.RunAsync("--version",
            workingDirectory: _temp.Path, environmentVariables: EnvWithHome());

        exitCode.Should().Be(0);
        stdout.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Help_ReturnsZeroAndContainsExpectedCommands()
    {
        var (exitCode, stdout, _) = await _cli.RunAsync("--help",
            workingDirectory: _temp.Path, environmentVariables: EnvWithHome());

        exitCode.Should().Be(0);
        stdout.Should().ContainAll("packs", "config", "status", "workspace");
    }

    [Fact]
    public async Task Status_ReturnsZero()
    {
        var (exitCode, _, _) = await _cli.RunAsync("status",
            workingDirectory: _temp.Path, environmentVariables: EnvWithHome());

        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task StatusJson_ReturnsZeroAndValidJson()
    {
        var (exitCode, stdout, _) = await _cli.RunAsync("status --json",
            workingDirectory: _temp.Path, environmentVariables: EnvWithHome());

        exitCode.Should().Be(0);

        var act = () => JsonDocument.Parse(stdout);
        act.Should().NotThrow("stdout should be valid JSON");
    }

    [Fact]
    public async Task ConfigShow_ReturnsZero()
    {
        var (exitCode, _, _) = await _cli.RunAsync("config show",
            workingDirectory: _temp.Path, environmentVariables: EnvWithHome());

        exitCode.Should().Be(0);
    }

    public void Dispose()
    {
        _temp.Dispose();
    }
}
