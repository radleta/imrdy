using System.Diagnostics;
using FluentAssertions;
using Imrdy.Integration.Tests.Helpers;
using Xunit;

namespace Imrdy.Integration.Tests;

/// <summary>
/// Parameterized smoke test that spawns <c>imrdy preview-dashboard</c> for every fixture
/// in <c>tests/fixtures/dashboards/</c>, asserts a visible window appears within 3 seconds,
/// sends WM_CLOSE, and asserts exit code 0.
/// </summary>
[Trait("Category", "Integration")]
public class PreviewAllFixturesTests
{
    public static IEnumerable<object[]> FixtureFiles() => FixtureCorpus.FixtureFiles();

    [Theory]
    [MemberData(nameof(FixtureFiles))]
    public async Task PreviewDashboard_AllFixtures_OpenAndCloseCleanly(string fixtureName, string fixturePath)
    {
        _ = fixtureName; // used as Theory display name

        File.Exists(fixturePath).Should().BeTrue($"fixture must exist at '{fixturePath}'");

        var cli = new CliTestFixture();

        var psi = new ProcessStartInfo
        {
            FileName = cli.BinaryPath,
            Arguments = $"preview-dashboard \"{fixturePath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start: {cli.BinaryPath}");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        // Wait up to 3s for a visible window belonging to the spawned process.
        nint windowHandle = nint.Zero;
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline && windowHandle == nint.Zero)
        {
            await Task.Delay(100);
            windowHandle = WindowHelper.FindVisibleWindowForProcess((uint)process.Id);
        }

        if (windowHandle != nint.Zero)
        {
            WindowHelper.PostMessage(windowHandle, WindowHelper.WM_CLOSE, nint.Zero, nint.Zero);
        }

        var exited = await Task.Run(() => process.WaitForExit(5000));
        if (!exited)
        {
            process.Kill(entireProcessTree: true);
            var stderrOnTimeout = await stderrTask;
            throw new TimeoutException(
                $"preview-dashboard '{fixtureName}' did not exit within 5s. stderr: {stderrOnTimeout}");
        }

        await stdoutTask;
        var stderrOutput = await stderrTask;

        process.ExitCode.Should().Be(0,
            $"preview-dashboard '{fixtureName}' should exit 0 on WM_CLOSE. stderr: {stderrOutput}");

        windowHandle.Should().NotBe(nint.Zero,
            $"a visible DashboardForm window should appear within 3 seconds for fixture '{fixtureName}'");
    }
}
