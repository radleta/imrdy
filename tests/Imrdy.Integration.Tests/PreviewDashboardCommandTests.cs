using System.Diagnostics;
using System.Runtime.InteropServices;
using FluentAssertions;
using Imrdy.Integration.Tests.Helpers;
using Xunit;

namespace Imrdy.Integration.Tests;

/// <summary>
/// Integration smoke test for <c>imrdy preview-dashboard</c>.
/// Spawns the published binary, waits for the DashboardForm window to appear,
/// sends WM_CLOSE to trigger a clean exit, and asserts exit code 0.
/// </summary>
[Trait("Category", "Integration")]
public class PreviewDashboardCommandTests
{
    private static readonly string FixturePath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "tests", "fixtures", "dashboards", "fresh-idle.json"));

    // Classic DllImport — no partial class / AllowUnsafeBlocks required.
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    private const uint WM_CLOSE = 0x0010;

    [Fact]
    public async Task PreviewDashboard_OpensWindowAndClosesCleanly()
    {
        // Fixture must exist (created as part of this step).
        File.Exists(FixturePath).Should().BeTrue($"fixture must exist at '{FixturePath}'");

        var cli = new CliTestFixture();

        var psi = new ProcessStartInfo
        {
            FileName = cli.BinaryPath,
            Arguments = $"preview-dashboard \"{FixturePath}\"",
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
            windowHandle = FindVisibleWindowForProcess((uint)process.Id);
        }

        if (windowHandle != nint.Zero)
        {
            // Clean close via WM_CLOSE — triggers Application.Run return → exit code 0.
            PostMessage(windowHandle, WM_CLOSE, nint.Zero, nint.Zero);
        }
        // If windowHandle is zero: still wait for the process to exit and assert below.
        // A zero handle means the window was not visible (possible on headless CI).

        var exited = await Task.Run(() => process.WaitForExit(5000));
        if (!exited)
        {
            process.Kill(entireProcessTree: true);
            var stderrOnTimeout = await stderrTask;
            throw new TimeoutException(
                $"preview-dashboard process did not exit within 5s. stderr: {stderrOnTimeout}");
        }

        await stdoutTask;   // drain to avoid broken-pipe
        var stderrOutput = await stderrTask;

        process.ExitCode.Should().Be(0,
            $"preview-dashboard should exit 0 on clean WM_CLOSE. stderr: {stderrOutput}");

        windowHandle.Should().NotBe(nint.Zero,
            "a visible DashboardForm window should appear within 3 seconds of launch");
    }

    [Fact]
    public async Task PreviewDashboard_MissingArg_ExitsOneWithStderr()
    {
        var cli = new CliTestFixture();

        var psi = new ProcessStartInfo
        {
            FileName = cli.BinaryPath,
            Arguments = "preview-dashboard",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start: {cli.BinaryPath}");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        var exited = await Task.Run(() => process.WaitForExit(5000));
        if (!exited)
            process.Kill(entireProcessTree: true);

        await stdoutTask;
        var stderr = await stderrTask;

        process.ExitCode.Should().Be(1, "missing path argument must exit 1");
        stderr.Should().NotBeNullOrWhiteSpace("a usage message should appear on stderr");
    }

    [Fact]
    public async Task PreviewDashboard_NonexistentFile_ExitsOneWithStderr()
    {
        var cli = new CliTestFixture();

        var psi = new ProcessStartInfo
        {
            FileName = cli.BinaryPath,
            Arguments = "preview-dashboard /nonexistent/path/fixture.json",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start: {cli.BinaryPath}");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        var exited = await Task.Run(() => process.WaitForExit(5000));
        if (!exited)
            process.Kill(entireProcessTree: true);

        await stdoutTask;
        var stderr = await stderrTask;

        process.ExitCode.Should().Be(1, "nonexistent file must exit 1");
        stderr.Should().Contain("not found", "error message should mention the missing file");
    }

    private nint FindVisibleWindowForProcess(uint pid)
    {
        nint found = nint.Zero;

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd))
                return true;    // continue enumeration

            GetWindowThreadProcessId(hWnd, out var windowPid);
            if (windowPid == pid)
            {
                found = hWnd;
                return false;   // stop enumeration
            }

            return true;
        }, nint.Zero);

        return found;
    }
}
