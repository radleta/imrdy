namespace Imrdy.Windows;

/// <summary>
/// Enumerates pre-existing state files in a WSL sessions directory at watcher arm time.
/// Extracted from TrayApp.ArmWslWatcher so unit tests can exercise the bootstrap path
/// without instantiating the full WinForms tray application.
/// </summary>
internal static class WslWatcherBootstrap
{
    /// <summary>
    /// Returns all *.json paths in <paramref name="uncSessionsPath"/>.
    /// Returns an empty list (does not throw) if the directory is missing or inaccessible —
    /// the watcher is still armed and will capture new events; any transient UNC gap is
    /// recoverable via the periodic 30s sweep.
    /// </summary>
    internal static IReadOnlyList<string> EnumerateExistingStateFiles(string uncSessionsPath)
    {
        try
        {
            return Directory.GetFiles(uncSessionsPath, "*.json");
        }
        catch (DirectoryNotFoundException)
        {
            return Array.Empty<string>();
        }
    }
}
