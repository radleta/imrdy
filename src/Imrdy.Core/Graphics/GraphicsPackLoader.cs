using System.Text.Json;

namespace Imrdy.Core.Graphics;

/// <summary>
/// Discovers and loads graphics packs from ~/.imrdy/graphics/packs/.
/// </summary>
public sealed class GraphicsPackLoader
{
    private static readonly HashSet<string> AllowedFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "svg",
        "png",
    };

    /// <summary>
    /// A loaded graphics pack with metadata and resolved state file paths.
    /// </summary>
    public sealed record LoadedGraphicsPack(
        string Name,
        string Version,
        string License,
        string Format,
        string PackDirectory,
        Dictionary<string, string> StateFilePaths);

    /// <summary>
    /// Discovers all graphics packs at the given packs root directory.
    /// Default: ~/.imrdy/graphics/packs/
    /// Returns an empty list if the root does not exist. Never throws.
    /// </summary>
    public IReadOnlyList<LoadedGraphicsPack> LoadPacks(string packsRoot)
    {
        if (!Directory.Exists(packsRoot))
        {
            return [];
        }

        var packs = new List<LoadedGraphicsPack>();
        foreach (var packDir in Directory.GetDirectories(packsRoot))
        {
            var packJsonPath = Path.Combine(packDir, "pack.json");
            var loaded = LoadPack(packDir, packJsonPath);
            if (loaded is not null)
            {
                packs.Add(loaded);
            }
        }

        return packs;
    }

    /// <summary>
    /// Loads a single graphics pack from a directory.
    /// Returns null if pack.json is missing, malformed, has an empty name,
    /// an unsupported format, no resolvable states, or any referenced state
    /// file escapes the pack directory or does not exist on disk.
    /// </summary>
    public LoadedGraphicsPack? LoadPack(string packDir, string packJsonPath)
    {
        if (!File.Exists(packJsonPath))
        {
            return null;
        }

        GraphicsPackJson? packJson;
        try
        {
            var bytes = File.ReadAllBytes(packJsonPath);
            packJson = JsonSerializer.Deserialize(bytes, ImrdyJsonContext.Default.GraphicsPackJson);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }

        if (packJson is null || string.IsNullOrEmpty(packJson.Name))
        {
            return null;
        }

        if (!AllowedFormats.Contains(packJson.Format))
        {
            return null;
        }

        var packDirFull = Path.GetFullPath(packDir);
        var packDirPrefix = packDirFull + Path.DirectorySeparatorChar;
        var stateFilePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (stateKey, stateConfig) in packJson.States)
        {
            if (string.IsNullOrEmpty(stateKey) || string.IsNullOrEmpty(stateConfig.File))
            {
                return null;
            }

            var stateFileFull = Path.GetFullPath(Path.Combine(packDir, stateConfig.File));
            if (!stateFileFull.StartsWith(packDirPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return null; // Reject state files that escape the pack directory
            }

            if (!File.Exists(stateFileFull))
            {
                return null;
            }

            stateFilePaths[stateKey] = stateFileFull;
        }

        if (stateFilePaths.Count == 0)
        {
            return null;
        }

        return new LoadedGraphicsPack(
            packJson.Name,
            packJson.Version,
            packJson.License,
            packJson.Format,
            packDirFull,
            stateFilePaths);
    }
}
