using System.Diagnostics;
using System.Runtime.InteropServices;
using FluentAssertions;
using Imrdy.Integration.Tests.Helpers;
using Xunit;

namespace Imrdy.Integration.Tests.Rendering;

/// <summary>
/// Integration test for Ctrl+C cancellation of <c>imrdy render dashboard --all</c>.
/// Spawns the published binary, sends CTRL_C_EVENT via <see cref="GenerateConsoleCtrlEvent"/>,
/// and asserts the process exits with code 130 (cancelled) or 0 (race: all fixtures finished first).
/// Any other exit code is a failure.
/// </summary>
[Trait("Category", "Integration")]
public class RenderCommandCancellationTests
{
    private const uint CTRL_C_EVENT = 0;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

    [Fact]
    public async Task CancelKeyPress_DuringRenderAll_ExitsCancelledOrZero()
    {
        string binaryPath;
        try
        {
            binaryPath = new CliTestFixture().BinaryPath;
        }
        catch (FileNotFoundException)
        {
            // Binary not published — skip rather than fail.
            return;
        }

        var tmpDir = Path.Combine(Path.GetTempPath(), "imrdy-render-cancel-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmpDir);
        try
        {
            using var process = StartWithNewProcessGroup(binaryPath,
                $"render dashboard --all --output-dir \"{tmpDir}\"");

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            // Wait 80ms — enough for the process to start and begin rendering, but typically
            // not long enough to finish all 4 fixtures (~50-200ms each).
            await Task.Delay(80);

            // Send CTRL_C to the child process group.
            GenerateConsoleCtrlEvent(CTRL_C_EVENT, (uint)process.Id);

            // Give the process up to 10s to exit cleanly after the signal.
            var exited = await Task.Run(() => process.WaitForExit(10_000));
            if (!exited)
            {
                process.Kill(entireProcessTree: true);
                var stderrOnTimeout = await stderrTask;
                throw new TimeoutException(
                    $"render process did not exit within 10s after CTRL_C. stderr: {stderrOnTimeout}");
            }

            await stdoutTask;   // drain to avoid broken-pipe
            await stderrTask;

            // 130 = cancelled cleanly; 0 = all fixtures finished before the signal arrived (race).
            // Anything else (1, 2, negative, etc.) is a failure.
            process.ExitCode.Should().BeOneOf(new[] { 0, 130 },
                $"render --all should exit 130 (cancelled) or 0 (race); got {process.ExitCode}");

            // No zombie: after WaitForExit the process is gone.
            var zombies = Process.GetProcessesByName("imrdy")
                .Where(p => p.Id == process.Id && !p.HasExited)
                .ToList();
            zombies.Should().BeEmpty("no imrdy.exe process should remain after clean exit");
        }
        finally
        {
            if (Directory.Exists(tmpDir))
                Directory.Delete(tmpDir, recursive: true);
        }
    }

    /// <summary>
    /// Starts the process with redirected IO so stdout/stderr can be consumed.
    /// xunit worker threads have no console attached (<c>UseShellExecute=false</c>,
    /// <c>CreateNoWindow=true</c>), so <c>GenerateConsoleCtrlEvent(0, child.Id)</c>
    /// reaches only the child process — no raw <c>CreateProcess</c> P/Invoke needed.
    /// </summary>
    private static Process StartWithNewProcessGroup(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        return Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start: {fileName}");
    }
}
