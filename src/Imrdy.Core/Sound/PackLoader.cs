using System.Text.Json;

namespace Imrdy.Core.Sound;

/// <summary>
/// Discovers and loads sound packs from ~/.claude/sounds/packs/.
/// </summary>
public sealed class PackLoader
{
    /// <summary>
    /// A loaded sound pack with metadata and WAV file inventory.
    /// </summary>
    public sealed record LoadedPack(
        string Name,
        string Description,
        string Version,
        string PackDirectory,
        PackJson PackJson,
        Dictionary<SoundEvent, string[]> WavFiles);

    /// <summary>
    /// Discovers all packs at the given packs root directory.
    /// Default: ~/.claude/sounds/packs/
    /// </summary>
    public IReadOnlyList<LoadedPack> LoadPacks(string packsRoot)
    {
        if (!Directory.Exists(packsRoot))
        {
            return [];
        }

        var packs = new List<LoadedPack>();
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
    /// Loads a single pack from a directory.
    /// Returns null if pack.json is missing or invalid.
    /// </summary>
    public LoadedPack? LoadPack(string packDir, string packJsonPath)
    {
        if (!File.Exists(packJsonPath))
        {
            return null;
        }

        PackJson? packJson;
        try
        {
            var bytes = File.ReadAllBytes(packJsonPath);
            packJson = JsonSerializer.Deserialize(bytes, ImrdyJsonContext.Default.PackJson);
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

        var wavFiles = new Dictionary<SoundEvent, string[]>();
        foreach (var (eventKey, eventConfig) in packJson.Events)
        {
            var soundEvent = SoundEventExtensions.FromFolderName(eventKey);
            if (soundEvent is null)
            {
                continue;
            }

            var eventFolder = Path.Combine(packDir, eventConfig.Folder);
            if (Directory.Exists(eventFolder))
            {
                var wavs = Directory.GetFiles(eventFolder, "*.wav");
                if (wavs.Length > 0)
                {
                    wavFiles[soundEvent.Value] = wavs;
                }
            }
        }

        return new LoadedPack(
            packJson.Name,
            packJson.Description,
            packJson.Version,
            packDir,
            packJson,
            wavFiles);
    }
}
