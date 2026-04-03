using System.Diagnostics;

namespace Imrdy.Integration.Tests.Helpers;

public sealed class CliTestFixture
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public string BinaryPath { get; }

    public CliTestFixture()
    {
        BinaryPath = Environment.GetEnvironmentVariable("IMRDY_BINARY_PATH")
            ?? Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..",
                "src", "Imrdy.Windows", "bin", "Release", "net10.0-windows", "win-x64", "publish", "imrdy.exe"));

        if (!File.Exists(BinaryPath))
            throw new FileNotFoundException(
                $"imrdy binary not found at '{BinaryPath}'. " +
                "Run 'dotnet publish src/Imrdy.Windows -c Release -r win-x64 --self-contained' first, " +
                "or set the IMRDY_BINARY_PATH environment variable.");
    }

    public async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(
        string args,
        string? stdin = null,
        string? workingDirectory = null)
    {
        workingDirectory ??= Path.GetTempPath();

        var psi = new ProcessStartInfo
        {
            FileName = BinaryPath,
            Arguments = args,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {BinaryPath} {args}");

        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin);
            process.StandardInput.Close();
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        var exited = await Task.Run(() => process.WaitForExit((int)DefaultTimeout.TotalMilliseconds));
        if (!exited)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"Process '{BinaryPath} {args}' did not exit within {DefaultTimeout.TotalSeconds}s.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return (process.ExitCode, stdout, stderr);
    }
}
