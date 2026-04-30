using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Imrdy.Core.Wsl;

public static class WslDistroDiscovery
{
    private const string PrimaryRoot = @"\\wsl.localhost\";

    // Test hook — set in unit tests to simulate the UNC root with a temp directory.
    internal static string? RootOverride { get; set; }

    // Test hook — when non-null, skips wsl.exe subprocess and uses the provided list.
    internal static Func<IReadOnlyList<string>>? RunningDistrosOverride { get; set; }

    public static Task<IReadOnlyList<DiscoveredDistro>> DiscoverAsync(
        CancellationToken ct = default,
        ILogger? logger = null)
        => Task.Run(() => DiscoverCore(ct, logger), ct);

    private static IReadOnlyList<DiscoveredDistro> DiscoverCore(CancellationToken ct, ILogger? logger)
    {
        var actualRoot = RootOverride ?? PrimaryRoot;

        var runningDistros = RunningDistrosOverride is not null
            ? RunningDistrosOverride()
            : GetRunningDistrosViaWsl(ct, logger);

        var result = new List<DiscoveredDistro>();
        foreach (var distroName in runningDistros)
        {
            ct.ThrowIfCancellationRequested();
            if (!IsValidDistroName(distroName))
            {
                logger?.LogDebug("WSL distro name rejected by validator: {Name}", distroName);
                continue;
            }
            try
            {
                var distroDir = Path.Combine(actualRoot, distroName);
                var homeRoot = Path.Combine(distroDir, "home");
                if (!Directory.Exists(homeRoot)) continue;

                var linuxHomes = new List<string>();
                foreach (var userDir in Directory.GetDirectories(homeRoot))
                {
                    var imrdyDir = Path.Combine(userDir, ".imrdy", "sessions");
                    if (Directory.Exists(imrdyDir))
                        linuxHomes.Add("/home/" + Path.GetFileName(userDir));
                }

                if (linuxHomes.Count > 0)
                    result.Add(new DiscoveredDistro(distroName, linuxHomes));
            }
            catch (Exception ex)
            {
                logger?.LogDebug(ex, "WSL distro probe skipped: {Distro}", distroName);
            }
        }

        logger?.LogInformation("WSL discovery: {Eligible} eligible distros from {Total} running",
            result.Count, runningDistros.Count);

        return result;
    }

    private static IReadOnlyList<string> GetRunningDistrosViaWsl(CancellationToken ct, ILogger? logger)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "wsl.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.Unicode,
        };
        psi.ArgumentList.Add("-l");
        psi.ArgumentList.Add("--running");
        psi.ArgumentList.Add("-q");

        Process? proc = null;
        try
        {
            proc = Process.Start(psi);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "wsl.exe could not be started");
            return Array.Empty<string>();
        }

        if (proc is null)
        {
            logger?.LogWarning("wsl.exe Process.Start returned null");
            return Array.Empty<string>();
        }

        using (proc)
        {
            string output;
            try
            {
                output = proc.StandardOutput.ReadToEnd();
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "wsl.exe stdout read failed");
                try { proc.Kill(entireProcessTree: true); } catch { }
                return Array.Empty<string>();
            }

            if (!proc.WaitForExit(5000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                logger?.LogWarning("wsl.exe timed out after 5s");
                return Array.Empty<string>();
            }

            if (proc.ExitCode != 0)
            {
                logger?.LogWarning("wsl.exe exited with code {Code}", proc.ExitCode);
                return Array.Empty<string>();
            }

            return output
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim().TrimStart('﻿'))
                .Where(line => line.Length > 0 && IsValidDistroName(line))
                .ToList();
        }
    }

    internal static bool IsValidDistroName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (name.Contains("..")) return false;
        if (name.IndexOfAny(['/', '\\']) >= 0) return false;
        if (name[0] == '.' || name[^1] == '.') return false;
        foreach (var c in name)
        {
            if (c < 0x20 || c == 0x7F) return false;
        }
        return true;
    }
}
