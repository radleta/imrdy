using FluentAssertions;
using Imrdy.Windows.Commands;
using Xunit;

namespace Imrdy.Integration.Tests.Rendering;

/// <summary>
/// Integration tests for the single-render path of <see cref="RenderCommand"/>:
/// <c>imrdy render &lt;component&gt; &lt;fixture&gt; [--output &lt;path&gt;]</c>.
///
/// STA threading: test methods that invoke the render path dispatch onto a fresh STA
/// thread so <see cref="System.Windows.Forms.Control.DrawToBitmap"/> works correctly.
///
/// Shares the RenderCommandConsole collection with RenderCommandHelpTests so that
/// Console.Out/Error redirects don't race across parallel test class execution.
/// </summary>
[Trait("Category", "Integration")]
[Collection("RenderCommandConsole")]
public class RenderCommandSingleTests
{
    private static string FixturePath(string name) => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "tests", "fixtures", "dashboards", name));

    /// <summary>
    /// Runs <see cref="RenderCommand.Run"/> on an STA thread and returns the exit code.
    /// stdout and stderr are captured via the supplied <see cref="StringWriter"/> instances.
    ///
    /// Console redirect is applied on the calling thread BEFORE the STA thread starts
    /// and restored AFTER it joins, so the STA thread inherits the redirect rather than
    /// racing with other tests that also mutate the global Console streams.
    /// </summary>
    private static int RunOnSta(string[] args, StringWriter stdout, StringWriter stderr)
    {
        int exitCode = -1;
        Exception? threadEx = null;

        // Set redirects on the calling thread so the STA thread inherits them
        var origOut = Console.Out;
        var origErr = Console.Error;
        Console.SetOut(stdout);
        Console.SetError(stderr);

        try
        {
            var thread = new Thread(() =>
            {
                try
                {
                    exitCode = RenderCommand.Run(args);
                }
                catch (Exception ex)
                {
                    threadEx = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }

        if (threadEx is not null)
            throw new InvalidOperationException($"STA thread threw: {threadEx.Message}", threadEx);

        return exitCode;
    }

    [Fact]
    public void Run_HappyPath_WritesPngAndReturnsZero()
    {
        var fixturePath = FixturePath("fresh-idle.json");
        var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");

        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        try
        {
            var exitCode = RunOnSta(
                ["render", "dashboard", fixturePath, "--output", outputPath],
                stdout, stderr);

            exitCode.Should().Be(0, because: $"happy-path render should succeed; stderr={stderr}");
            stderr.ToString().Should().BeEmpty(because: "no errors expected on success");

            var stdoutText = stdout.ToString();
            stdoutText.Should().Contain("dashboard/", because: "summary line must include 'dashboard/'");
            // Dimensions in WxH format — pattern: one or more digits x one or more digits
            stdoutText.Should().MatchRegex(@"\d+x\d+", because: "summary line must include WxH dimensions");

            File.Exists(outputPath).Should().BeTrue("output PNG must be written to disk");
            new FileInfo(outputPath).Length.Should().BeGreaterThan(500,
                because: "a valid non-empty PNG must be at least 500 bytes");
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    [Fact]
    public void Run_UnknownComponent_WritesUnknownComponentToStderrAndReturnsOne()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = RunOnSta(
            ["render", "no-such-component", "some-fixture.json"],
            stdout, stderr);

        exitCode.Should().Be(1, because: "unknown component is a user error");
        stderr.ToString().Should().Contain("unknown component",
            because: "message must tell the user what went wrong");
    }

    [Fact]
    public void Run_MissingFixtureArg_WritesFixtureMessageToStderrAndReturnsOne()
    {
        // No positional args after the component name
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = RunOnSta(
            ["render", "dashboard"],
            stdout, stderr);

        exitCode.Should().Be(1, because: "missing fixture is a user error");
        stderr.ToString().Should().Contain("fixture",
            because: "error message must mention the missing fixture argument");
    }

    [Fact]
    public void Run_MissingFixtureArgWithOutput_WritesFixtureMessageToStderrAndReturnsOne()
    {
        // --output is present but no positional fixture was supplied
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = RunOnSta(
            ["render", "dashboard", "--output", "x.png"],
            stdout, stderr);

        exitCode.Should().Be(1, because: "missing fixture with --output is a user error");
        stderr.ToString().Should().Contain("fixture",
            because: "error message must mention the missing fixture argument");
    }

    [Fact]
    public void Run_UnknownFlag_WritesUnknownFlagToStderrAndReturnsOne()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = RunOnSta(
            ["render", "dashboard", "--not-a-real-flag"],
            stdout, stderr);

        exitCode.Should().Be(1, because: "unknown flag is a user error");
        stderr.ToString().Should().Contain("unknown flag",
            because: "message must tell the user the flag is unrecognized");
    }

    [Fact]
    public void Run_OutputAndOutputDirBothSet_WritesCannotCombineToStderrAndReturnsOne()
    {
        var fixturePath = FixturePath("fresh-idle.json");

        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = RunOnSta(
            ["render", "dashboard", fixturePath, "--output", "a.png", "--output-dir", "/tmp"],
            stdout, stderr);

        exitCode.Should().Be(1, because: "--output + --output-dir collision is a user error");
        stderr.ToString().Should().Contain("cannot combine",
            because: "message must describe the mutual exclusion");
    }
}
