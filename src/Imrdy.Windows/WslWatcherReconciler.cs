using Imrdy.Core.Wsl;

namespace Imrdy.Windows;

/// <summary>
/// Pure reconciliation logic for the WSL multi-watcher lifecycle.
/// Extracted from TrayApp so unit tests can exercise it without instantiating
/// the full WinForms tray application.
/// </summary>
internal static class WslWatcherReconciler
{
    /// <summary>
    /// Computes the (toArm, toDisarm) key sets given the current watcher keys,
    /// the freshly discovered distros, and the loaded WSL distro config.
    /// </summary>
    /// <param name="currentKeys">Keys currently armed in _wslWatchers.</param>
    /// <param name="distros">Discovery results from WslDistroDiscovery.DiscoverAsync.</param>
    /// <param name="config">Current WslDistroConfig (after Reconcile has been called).</param>
    /// <param name="toArm">Keys that need a new FileSystemWatcher.</param>
    /// <param name="toDisarm">Keys whose FileSystemWatcher should be disposed.</param>
    internal static void ComputeDelta(
        IEnumerable<(string Distro, string User)> currentKeys,
        IReadOnlyList<DiscoveredDistro> distros,
        WslDistroConfig config,
        out HashSet<(string Distro, string User)> toArm,
        out List<(string Distro, string User)> toDisarm)
    {
        var targetKeys = BuildTargetKeys(distros, config);

        var current = new HashSet<(string Distro, string User)>(currentKeys);
        toArm = new HashSet<(string Distro, string User)>(targetKeys);
        toArm.ExceptWith(current);

        toDisarm = current.Where(k => !targetKeys.Contains(k)).ToList();
    }

    /// <summary>
    /// Builds the UNC sessions path for a given distro name and linux home path.
    /// </summary>
    internal static string BuildUncPath(string distroName, string linuxHome)
        => $@"\\wsl.localhost\{distroName}{linuxHome.Replace('/', '\\')}\.imrdy\sessions";

    private static HashSet<(string Distro, string User)> BuildTargetKeys(
        IReadOnlyList<DiscoveredDistro> distros,
        WslDistroConfig config)
    {
        var result = new HashSet<(string Distro, string User)>();
        foreach (var d in distros)
        {
            var shouldArm = config.WatchAll;
            if (!shouldArm)
            {
                var entry = config.Distros?.FirstOrDefault(e => e.Name == d.Name);
                shouldArm = entry?.Enabled == true;
            }

            if (!shouldArm) continue;

            foreach (var linuxHome in d.LinuxHomes)
                result.Add((d.Name, linuxHome));
        }

        return result;
    }
}
