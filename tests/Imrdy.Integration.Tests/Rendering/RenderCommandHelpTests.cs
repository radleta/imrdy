using FluentAssertions;
using Imrdy.Windows.Commands;
using Xunit;

namespace Imrdy.Integration.Tests.Rendering;

/// <summary>
/// Smoke tests for <see cref="RenderCommand"/>'s discoverability paths (--help, --list).
/// Shares the RenderCommandConsole collection with RenderCommandSingleTests so that
/// Console.Out/Error redirects don't race across parallel test class execution.
/// </summary>
[Trait("Category", "Integration")]
[Collection("RenderCommandConsole")]
public class RenderCommandHelpTests
{
    [Fact]
    public void Run_EmptyArgs_PrintsHelpAndReturnsZero()
    {
        using var sw = new StringWriter();
        var original = Console.Out;
        Console.SetOut(sw);
        try
        {
            var exitCode = RenderCommand.Run(Array.Empty<string>());
            exitCode.Should().Be(0, "empty args falls through to help path");
            sw.ToString().Should().Contain("Usage:");
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Theory]
    [InlineData("render", null)]
    [InlineData("render", "--help")]
    [InlineData("render", "-h")]
    public void Run_HelpPath_WritesUsageToStdoutAndReturnsZero(string verb, string? flag)
    {
        var args = flag is null ? new[] { verb } : new[] { verb, flag };
        using var sw = new StringWriter();
        var original = Console.Out;
        Console.SetOut(sw);
        try
        {
            var exitCode = RenderCommand.Run(args);
            exitCode.Should().Be(0);
            sw.ToString().Should().Contain("Usage:");
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Fact]
    public void Run_ListPath_WritesComponentTableToStdoutAndReturnsZero()
    {
        using var sw = new StringWriter();
        var original = Console.Out;
        Console.SetOut(sw);
        try
        {
            var exitCode = RenderCommand.Run(new[] { "render", "--list" });
            exitCode.Should().Be(0);
            sw.ToString().Should().Contain("dashboard");
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Fact]
    public void Run_UnrecognizedArgs_WritesToStderrAndReturnsUserError()
    {
        using var sw = new StringWriter();
        var original = Console.Error;
        Console.SetError(sw);
        try
        {
            var exitCode = RenderCommand.Run(new[] { "render", "--unknown-flag" });
            exitCode.Should().Be(1, "unrecognized flags return ExitUserError");
            sw.ToString().Should().Contain("render:");
        }
        finally
        {
            Console.SetError(original);
        }
    }
}
