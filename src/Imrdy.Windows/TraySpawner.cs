using System.Diagnostics;
using System.Security.AccessControl;
using Imrdy.Core;
using Microsoft.Extensions.Logging;

namespace Imrdy.Windows;

/// <summary>
/// Mutex-gated tray auto-spawner. Probes the global mutex to determine
/// if the tray is already running, and spawns a new instance if not.
/// Designed to be called from the hook fast-path — never throws.
/// </summary>
internal static class TraySpawner
{
    /// <summary>
    /// Ensures the tray app is running. Returns true if it was spawned,
    /// false if already running or spawn was skipped/failed.
    /// </summary>
    public static bool EnsureRunning(ILogger logger)
    {
        // IMRDY_NO_TRAY=1 suppresses tray spawn (headless CI, containers, SSH).
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IMRDY_NO_TRAY")))
        {
            logger.LogDebug("Tray spawn suppressed by IMRDY_NO_TRAY");
            return false;
        }

        try
        {
            // Probe the mutex — if it exists, tray is already running
            if (MutexAcl.TryOpenExisting(ImrdyPaths.MutexName, MutexRights.Synchronize, out var mutex))
            {
                mutex.Dispose();
                return false;
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Mutex exists but we can't open it (elevation mismatch) — treat as running
            logger.LogWarning("Mutex exists but access denied (elevation mismatch) — tray is running");
            return false;
        }

        // Mutex not found — spawn the tray
        try
        {
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(processPath))
            {
                logger.LogWarning("Cannot determine process path for tray spawn");
                return false;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = processPath,
                Arguments = "",
                // ShellExecuteEx fully detaches the child — no handle inheritance.
                // CreateProcess (UseShellExecute=false) inherits all handles even
                // with redirected streams, which keeps Claude Code's hook pipe open.
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            using var process = Process.Start(startInfo);
            if (process is not null)
            {
                logger.LogInformation("Tray started");
                return true;
            }

            logger.LogWarning("Process.Start returned null");
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Tray spawn failed");
            return false;
        }
    }
}
