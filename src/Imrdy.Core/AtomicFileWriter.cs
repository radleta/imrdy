namespace Imrdy.Core;

/// <summary>
/// Writes files atomically using a temp-file + delete-then-move pattern.
/// Ensures FileSystemWatcher reliably fires Created events on Windows.
/// </summary>
public static class AtomicFileWriter
{
    /// <summary>
    /// Writes content atomically using delete-then-move pattern.
    /// Creates parent directory if missing.
    /// Uses delete-then-move (NOT File.Move overwrite:true) to ensure FSW fires reliably.
    /// </summary>
    public static void Write(string path, byte[] content)
    {
        var dir = Path.GetDirectoryName(path);
        if (dir is not null)
            Directory.CreateDirectory(dir);

        var tmpPath = path + ".tmp";
        File.WriteAllBytes(tmpPath, content);

        // Delete-then-move: ensures FileSystemWatcher sees a Created event.
        // File.Move(overwrite:true) suppresses FSW notifications on Windows.
        if (File.Exists(path))
            File.Delete(path);
        File.Move(tmpPath, path);
    }
}
