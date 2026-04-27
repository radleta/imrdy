using FluentAssertions;
using Imrdy.Integration.Tests.Helpers;
using Xunit;

namespace Imrdy.Integration.Tests.Rendering;

/// <summary>
/// Binary smoke tests for <c>imrdy render</c> — spawns the published binary via
/// <see cref="CliTestFixture"/> and asserts end-to-end CLI behaviour. Tests skip
/// gracefully when the binary is not yet published (matching
/// <see cref="RenderCommandCancellationTests"/> pattern).
/// </summary>
[Trait("Category", "Integration")]
public class RenderCommandBinaryTests
{
    private static readonly string FixturePath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "tests", "fixtures", "dashboards", "fresh-idle.json"));

    [Fact]
    public async Task RenderHelp_ExitsZeroWithUsageOnStdout()
    {
        CliTestFixture cli;
        try
        {
            cli = new CliTestFixture();
        }
        catch (FileNotFoundException)
        {
            return;
        }

        var (exitCode, stdout, _) = await cli.RunAsync("render --help");

        exitCode.Should().Be(0, "render --help must exit 0");
        stdout.Should().Contain("Usage:", "render --help must print a usage line");
    }

    [Fact]
    public async Task RenderList_ExitsZeroWithDashboardOnStdout()
    {
        CliTestFixture cli;
        try
        {
            cli = new CliTestFixture();
        }
        catch (FileNotFoundException)
        {
            return;
        }

        var (exitCode, stdout, _) = await cli.RunAsync("render --list");

        exitCode.Should().Be(0, "render --list must exit 0");
        stdout.Should().Contain("dashboard", "render --list must list the dashboard component");
    }

    [Fact]
    public async Task RenderDashboard_WithFixtureAndOutput_WritesPngAndExitsZero()
    {
        CliTestFixture cli;
        try
        {
            cli = new CliTestFixture();
        }
        catch (FileNotFoundException)
        {
            return;
        }

        File.Exists(FixturePath).Should().BeTrue($"fixture must exist at '{FixturePath}'");

        var outputPng = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        try
        {
            var (exitCode, _, stderr) = await cli.RunAsync(
                $"render dashboard \"{FixturePath}\" --output \"{outputPng}\"");

            exitCode.Should().Be(0, $"render dashboard must exit 0. stderr: {stderr}");
            File.Exists(outputPng).Should().BeTrue("render must create the output PNG");
            new FileInfo(outputPng).Length.Should().BeGreaterThan(500,
                "rendered PNG must be at least 500 bytes (valid non-empty image)");
        }
        finally
        {
            if (File.Exists(outputPng))
                File.Delete(outputPng);
        }
    }

    [Fact]
    public async Task RenderBogusComponent_ExitsOneWithStderrMessage()
    {
        CliTestFixture cli;
        try
        {
            cli = new CliTestFixture();
        }
        catch (FileNotFoundException)
        {
            return;
        }

        var (exitCode, _, stderr) = await cli.RunAsync("render bogus-component");

        exitCode.Should().Be(1, "unknown component must exit 1 (ExitUserError)");
        stderr.Should().Contain("unknown component",
            "stderr must describe why the command failed");
    }
}
