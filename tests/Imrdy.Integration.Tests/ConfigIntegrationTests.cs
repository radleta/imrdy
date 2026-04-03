using FluentAssertions;
using Imrdy.Integration.Tests.Helpers;
using Xunit;

namespace Imrdy.Integration.Tests;

public class ConfigIntegrationTests : IDisposable
{
    private readonly CliTestFixture _cli = new();
    private readonly TempDirectoryFixture _temp = new();

    [Fact]
    public async Task ConfigSet_SoundEnabledFalse_ExitsZero()
    {
        var (exitCode, _, _) = await _cli.RunAsync("config set soundEnabled false", workingDirectory: _temp.Path);

        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task ConfigSet_ThenShow_ReflectsValue()
    {
        // Set soundEnabled to false
        var (setExit, _, _) = await _cli.RunAsync("config set soundEnabled false", workingDirectory: _temp.Path);
        setExit.Should().Be(0);

        // Show config and verify
        var (showExit, stdout, _) = await _cli.RunAsync("config show", workingDirectory: _temp.Path);
        showExit.Should().Be(0);
        stdout.Should().Contain("false", "config show should reflect the set value");

        // Set soundEnabled to true
        var (setExit2, _, _) = await _cli.RunAsync("config set soundEnabled true", workingDirectory: _temp.Path);
        setExit2.Should().Be(0);

        // Show config again
        var (showExit2, stdout2, _) = await _cli.RunAsync("config show", workingDirectory: _temp.Path);
        showExit2.Should().Be(0);
        stdout2.Should().Contain("true", "config show should reflect the updated value");
    }

    public void Dispose()
    {
        _temp.Dispose();
    }
}
