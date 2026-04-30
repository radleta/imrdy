using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Imrdy.Core.Wsl;

/// <summary>
/// Reads/writes ~/.imrdy/wsl-distros.json with atomic writes (tmp + rename).
/// Creates the directory if missing.
/// </summary>
public sealed class WslDistroStore
{
    private readonly string _filePath;

    public WslDistroStore(string filePath)
    {
        _filePath = filePath;
    }

    /// <summary>
    /// Returns true when a linux_home value contains a '..' path segment that
    /// could escape the distro UNC root when concatenated in BuildUncPath.
    /// </summary>
    private static bool ContainsDotDotSegment(string linuxHome)
    {
        var normalized = linuxHome.TrimStart('/');
        var segments = normalized.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        return Array.Exists(segments, s => s == "..");
    }

    /// <summary>
    /// Loads the WSL distro config from disk. Returns an empty config if the file
    /// doesn't exist or is corrupt. Filters out any linux_home values that contain
    /// '..' path segments to prevent UNC path traversal.
    /// </summary>
    public WslDistroConfig Load(ILogger? logger = null)
    {
        if (!File.Exists(_filePath))
            return new WslDistroConfig();

        WslDistroConfig config;
        try
        {
            var bytes = File.ReadAllBytes(_filePath);
            config = JsonSerializer.Deserialize(bytes, ImrdyJsonContext.Default.WslDistroConfig)
                     ?? new WslDistroConfig();
        }
        catch (JsonException) { return new WslDistroConfig(); }
        catch (IOException)   { return new WslDistroConfig(); }

        // Drop any linux_home values that contain '..' segments — these bypass the
        // CLI guard in WslCommand and could escape \\wsl.localhost\<distro>\ via BuildUncPath.
        if (config.Distros is null)
            return config;

        var distros = config.Distros;
        var sanitized = false;
        for (var i = 0; i < distros.Count; i++)
        {
            var entry = distros[i];
            if (entry.LinuxHomes is null) continue;

            List<string>? filtered = null;
            foreach (var home in entry.LinuxHomes)
            {
                if (ContainsDotDotSegment(home))
                {
                    logger?.LogWarning(
                        "WslDistroStore.Load: dropped invalid linux_home '{Value}' (contains '..' segments) from distro '{Name}'",
                        home, entry.Name);
                    sanitized = true;
                }
                else
                {
                    (filtered ??= []).Add(home);
                }
            }

            if (filtered?.Count != entry.LinuxHomes.Count)
                distros[i] = entry with { LinuxHomes = filtered };
        }

        return sanitized ? config with { Distros = distros } : config;
    }

    /// <summary>
    /// Saves the WSL distro config atomically (tmp + rename).
    /// Creates the parent directory if it doesn't exist.
    /// </summary>
    public void Save(WslDistroConfig config)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(config, ImrdyJsonContext.Default.WslDistroConfig);
        AtomicFileWriter.Write(_filePath, json);
    }

    /// <summary>
    /// Adds a distro entry or appends linuxHome to an existing entry's linux_homes.
    /// Deduplicates linuxHome within the entry. No-op when name+linuxHome already exist.
    /// </summary>
    public void Add(string name, string? linuxHome)
    {
        var config = Load();
        var distros = config.Distros ?? [];
        var index = distros.FindIndex(e => e.Name == name);

        if (index < 0)
        {
            List<string>? homes = linuxHome is not null ? [linuxHome] : null;
            distros.Add(new WslDistroEntry
            {
                Name = name,
                LinuxHomes = homes,
                Enabled = config.WatchAll,
                DiscoveredAt = DateTimeOffset.UtcNow,
            });
        }
        else if (linuxHome is not null)
        {
            var entry = distros[index];
            var homes = entry.LinuxHomes ?? [];
            if (!homes.Contains(linuxHome))
            {
                homes.Add(linuxHome);
                distros[index] = entry with { LinuxHomes = homes };
            }
            else
            {
                return; // same name + same linuxHome: no-op
            }
        }
        else
        {
            return; // null linuxHome and entry exists: no-op
        }

        Save(config with { Distros = distros });
    }

    /// <summary>
    /// Removes a distro entry by name. No-op if not found.
    /// </summary>
    public void Remove(string name)
    {
        var config = Load();
        var distros = config.Distros;
        if (distros is null) return;

        var removed = distros.RemoveAll(e => e.Name == name);
        if (removed > 0)
            Save(config with { Distros = distros });
    }

    /// <summary>
    /// Updates the top-level watch_all flag.
    /// </summary>
    public void SetWatchAll(bool watchAll)
    {
        var config = Load();
        Save(config with { WatchAll = watchAll });
    }

    /// <summary>
    /// Updates the enabled flag for a named distro. No-op if not found.
    /// </summary>
    public void SetEnabled(string name, bool enabled)
    {
        var config = Load();
        var distros = config.Distros;
        if (distros is null) return;

        var index = distros.FindIndex(e => e.Name == name);
        if (index < 0) return;

        distros[index] = distros[index] with { Enabled = enabled };
        Save(config with { Distros = distros });
    }

    /// <summary>
    /// Merges discovery results into the store. Non-destructive: existing entries not in the
    /// discovered list are preserved. New entries are added with Enabled = WatchAll.
    /// Existing entries have their linux_homes updated additively (no duplicates).
    /// </summary>
    public void Reconcile(IReadOnlyList<DiscoveredDistro> discovered)
    {
        var config = Load();
        var distros = config.Distros ?? [];
        var now = DateTimeOffset.UtcNow;
        var changed = false;

        foreach (var d in discovered)
        {
            var index = distros.FindIndex(e => e.Name == d.Name);
            if (index < 0)
            {
                distros.Add(new WslDistroEntry
                {
                    Name = d.Name,
                    LinuxHomes = d.LinuxHomes.Count > 0 ? [.. d.LinuxHomes] : null,
                    Enabled = config.WatchAll,
                    DiscoveredAt = now,
                });
                changed = true;
            }
            else
            {
                var entry = distros[index];
                var homes = entry.LinuxHomes ?? [];
                var added = false;
                foreach (var home in d.LinuxHomes)
                {
                    if (!homes.Contains(home))
                    {
                        homes.Add(home);
                        added = true;
                    }
                }

                if (added)
                {
                    distros[index] = entry with { LinuxHomes = homes, DiscoveredAt = now };
                    changed = true;
                }
            }
        }

        if (changed)
            Save(config with { Distros = distros });
    }
}
