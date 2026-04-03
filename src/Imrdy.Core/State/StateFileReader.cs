using System.Text.Json;

namespace Imrdy.Core.State;

/// <summary>
/// Reads and writes session state JSON files with BOM handling and error tolerance.
/// </summary>
public sealed class StateFileReader
{
    /// <summary>
    /// Reads a state file from disk. Returns null if the file doesn't exist,
    /// is corrupt, or is mid-write.
    /// </summary>
    public StateFileModel? ReadStateFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var bytes = File.ReadAllBytes(path);
            bytes = StripBom(bytes);
            return JsonSerializer.Deserialize(bytes, ImrdyJsonContext.Default.StateFileModel);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes a state file directly. Uses UTF-8 without BOM.
    /// Direct write (not temp+rename) ensures FileSystemWatcher fires Changed events.
    /// The JSON reader handles partial reads gracefully, and files are small (~300 bytes).
    /// </summary>
    public void WriteStateFile(string path, StateFileModel model)
    {
        var dir = Path.GetDirectoryName(path);
        if (dir is not null && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.SerializeToUtf8Bytes(model, ImrdyJsonContext.Default.StateFileModel);
        File.WriteAllBytes(path, json);
    }

    /// <summary>
    /// Reads all state files from a sessions directory.
    /// </summary>
    public IReadOnlyList<StateFileModel> ReadAllStateFiles(string sessionsDir)
    {
        if (!Directory.Exists(sessionsDir))
        {
            return [];
        }

        var results = new List<StateFileModel>();
        foreach (var file in Directory.GetFiles(sessionsDir, "*.json"))
        {
            var model = ReadStateFile(file);
            if (model is not null)
            {
                results.Add(model);
            }
        }

        return results;
    }

    /// <summary>
    /// Removes a state file and its associated PID cache file.
    /// </summary>
    public void RemoveStateFile(string sessionsDir, string sessionId)
    {
        var statePath = Path.Combine(sessionsDir, $"{sessionId}.json");
        TryDelete(statePath);

        var pidPath = Path.Combine(sessionsDir, $".pid-{sessionId}");
        TryDelete(pidPath);
    }

    /// <summary>
    /// Strips UTF-8 BOM bytes (0xEF, 0xBB, 0xBF) from the start of a byte array.
    /// Legacy PS1-touched files may have BOM bytes.
    /// </summary>
    private static byte[] StripBom(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return bytes[3..];
        }

        return bytes;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup — file may be locked
        }
    }
}
