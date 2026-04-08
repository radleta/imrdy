using System.Text.Json;
using Imrdy.Core.Desktop;

namespace Imrdy.Core.Workspace;

/// <summary>
/// Reads/writes ~/.imrdy/workspaces.json with atomic writes (tmp + rename).
/// Creates the directory if missing.
/// </summary>
public sealed class WorkspaceStore
{
    private readonly string _filePath;

    public WorkspaceStore(string filePath)
    {
        _filePath = filePath;
    }

    /// <summary>
    /// Loads the workspace config from disk. Returns an empty config if the file
    /// doesn't exist or is corrupt.
    /// </summary>
    public WorkspaceConfig Load()
    {
        if (!File.Exists(_filePath))
        {
            return new WorkspaceConfig();
        }

        try
        {
            var bytes = File.ReadAllBytes(_filePath);
            return JsonSerializer.Deserialize(bytes, ImrdyJsonContext.Default.WorkspaceConfig)
                   ?? new WorkspaceConfig();
        }
        catch (JsonException)
        {
            return new WorkspaceConfig();
        }
        catch (IOException)
        {
            return new WorkspaceConfig();
        }
    }

    /// <summary>
    /// Saves the workspace config atomically (tmp + rename).
    /// Creates the parent directory if it doesn't exist.
    /// </summary>
    public void Save(WorkspaceConfig config)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(config, ImrdyJsonContext.Default.WorkspaceConfig);
        AtomicFileWriter.Write(_filePath, json);
    }

    /// <summary>
    /// Returns true if the given path is pinned as a workspace.
    /// </summary>
    public bool IsPinned(string path)
    {
        var config = Load();
        return config.Workspaces.Any(w => PathNormalizer.AreEqual(w.Path, path));
    }

    /// <summary>
    /// Pins a workspace. If the path is already pinned, updates name and desktop.
    /// </summary>
    public void Pin(string path, string name, int desktop)
    {
        var config = Load();
        var normalized = PathNormalizer.Normalize(path);
        var existing = config.Workspaces.FindIndex(
            w => PathNormalizer.AreEqual(w.Path, normalized));

        if (existing >= 0)
        {
            config.Workspaces[existing] = new WorkspaceEntry
            {
                Path = normalized,
                Name = name,
                Desktop = desktop,
            };
        }
        else
        {
            config.Workspaces.Add(new WorkspaceEntry
            {
                Path = normalized,
                Name = name,
                Desktop = desktop,
            });
        }

        Save(config);
    }

    /// <summary>
    /// Unpins a workspace by path. No-op if not found.
    /// </summary>
    public void Unpin(string path)
    {
        var config = Load();
        var removed = config.Workspaces.RemoveAll(
            w => PathNormalizer.AreEqual(w.Path, path));

        if (removed > 0)
        {
            Save(config);
        }
    }

    /// <summary>
    /// Updates the desktop assignment for a workspace. No-op if not found.
    /// </summary>
    public void SetDesktop(string path, int desktop)
    {
        var config = Load();
        var index = config.Workspaces.FindIndex(
            w => PathNormalizer.AreEqual(w.Path, path));

        if (index >= 0)
        {
            config.Workspaces[index] = config.Workspaces[index] with { Desktop = desktop };
            Save(config);
        }
    }
}
