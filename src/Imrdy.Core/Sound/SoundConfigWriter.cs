using System.Text.Json;

namespace Imrdy.Core.Sound;

/// <summary>
/// Shared atomic-write helper for SoundConfig persistence.
/// Writes to a .tmp file then renames, preventing partial reads.
/// </summary>
public static class SoundConfigWriter
{
    /// <summary>
    /// Atomically writes a SoundConfig to the specified path.
    /// Creates the parent directory if missing.
    /// </summary>
    /// <param name="config">The config to persist.</param>
    /// <param name="path">Destination file path (e.g. ~/.claude/sounds/config.json).</param>
    public static void Save(SoundConfig config, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (dir is not null && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tmpPath = path + ".tmp";
        var json = JsonSerializer.SerializeToUtf8Bytes(config, ImrdyJsonContext.Default.SoundConfig);
        File.WriteAllBytes(tmpPath, json);
        File.Move(tmpPath, path, overwrite: true);
    }
}
