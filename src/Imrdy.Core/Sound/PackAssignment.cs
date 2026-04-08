namespace Imrdy.Core.Sound;

/// <summary>
/// Resolves which sound pack to use for a session via a 5-level priority chain:
/// 1. State file override (sound_pack field)
/// 2. Config project mapping
/// 3. Config default ("random" picks from enabled packs, "" means none)
/// 4. CLI --sound-pack param
/// 5. Auto-detect (single enabled pack with WAVs)
/// Disabled packs are excluded from all resolution except explicit state file overrides.
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
        // Priority 1: State file override (respects even disabled packs — explicit user choice)
        if (!string.IsNullOrEmpty(stateFileOverride) && PackExists(stateFileOverride))
        {
            return stateFileOverride;
        }

        // Priority 2: Config project mapping (must be enabled)
        if (!string.IsNullOrEmpty(project) && _config.Projects is not null
            && _config.Projects.TryGetValue(project, out var projectPack)
            && EnabledPackExists(projectPack))
        {
            return projectPack;
        }

        // Priority 3: Config default
        if (!string.IsNullOrEmpty(_config.DefaultPack))
        {
            if (string.Equals(_config.DefaultPack, "random", StringComparison.OrdinalIgnoreCase))
            {
                return PickRandomEnabledPack();
            }

            if (EnabledPackExists(_config.DefaultPack))
            {
                return _config.DefaultPack;
            }
        }

        // Priority 4: CLI --sound-pack param (must be enabled)
        if (!string.IsNullOrEmpty(_cliDefault) && EnabledPackExists(_cliDefault))
        {
            return _cliDefault;
        }

        // Priority 5: Auto-detect (single enabled pack with WAVs)
        var enabledWithWavs = GetEnabledPacksWithWavs();
        if (enabledWithWavs.Count == 1)
        {
            return enabledWithWavs[0].Name;
        }

        return null;
    }

    private string? PickRandomEnabledPack()
    {
        var candidates = GetEnabledPacksWithWavs();
        if (candidates.Count == 0)
        {
            return null;
        }

        return candidates[Random.Shared.Next(candidates.Count)].Name;
    }

    private List<PackLoader.LoadedPack> GetEnabledPacksWithWavs()
    {
        var disabled = _config.DisabledPacks;
        return _packs
            .Where(p => p.WavFiles.Count > 0
                && !disabled.Any(d => string.Equals(d, p.Name, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private bool PackExists(string name)
    {
        return _packs.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private bool EnabledPackExists(string name)
    {
        return PackExists(name)
            && !_config.DisabledPacks.Any(d => string.Equals(d, name, StringComparison.OrdinalIgnoreCase));
    }
}
