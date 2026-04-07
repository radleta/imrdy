namespace Imrdy.Core.Sound;

/// <summary>
/// Resolves which sound pack to use for a session via a 5-level priority chain:
/// 1. State file override (sound_pack field)
/// 2. Config project mapping
/// 3. Config default
/// 4. CLI --sound-pack param
/// 5. Auto-detect (single pack with WAVs)
/// </summary>
public sealed class PackAssignment
{
    private readonly IReadOnlyList<PackLoader.LoadedPack> _packs;
    private readonly SoundConfig _config;
    private readonly string? _cliDefault;

    public PackAssignment(
        IReadOnlyList<PackLoader.LoadedPack> packs,
        SoundConfig? config = null,
        string? cliDefault = null)
    {
        _packs = packs;
        _config = config ?? new SoundConfig();
        _cliDefault = cliDefault;
    }

    /// <summary>
    /// Resolves the pack name for a given session.
    /// </summary>
    /// <param name="stateFileOverride">The sound_pack field from the session state file.</param>
    /// <param name="project">The project name (for project-level config mapping).</param>
    /// <returns>The resolved pack name, or null if no pack could be determined.</returns>
    public string? Resolve(string? stateFileOverride, string? project)
    {
        // Priority 1: State file override
        if (!string.IsNullOrEmpty(stateFileOverride) && PackExists(stateFileOverride))
        {
            return stateFileOverride;
        }

        // Priority 2: Config project mapping
        if (!string.IsNullOrEmpty(project) && _config.Projects is not null
            && _config.Projects.TryGetValue(project, out var projectPack)
            && PackExists(projectPack))
        {
            return projectPack;
        }

        // Priority 3: Config default
        if (!string.IsNullOrEmpty(_config.DefaultPack) && PackExists(_config.DefaultPack))
        {
            return _config.DefaultPack;
        }

        // Priority 4: CLI --sound-pack param
        if (!string.IsNullOrEmpty(_cliDefault) && PackExists(_cliDefault))
        {
            return _cliDefault;
        }

        // Priority 5: Auto-detect (single pack with WAVs)
        var packsWithWavs = _packs.Where(p => p.WavFiles.Count > 0).ToList();
        if (packsWithWavs.Count == 1)
        {
            return packsWithWavs[0].Name;
        }

        return null;
    }

    private bool PackExists(string name)
    {
        return _packs.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
