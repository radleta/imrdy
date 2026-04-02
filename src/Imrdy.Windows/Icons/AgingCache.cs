using System.Drawing;

namespace Imrdy.Windows.Icons;

/// <summary>
/// Caches rendered tray icons by status + aging factor to avoid GDI+ churn.
/// Disposes evicted entries when a cache key is replaced.
/// </summary>
internal sealed class AgingCache : IDisposable
{
    private readonly Dictionary<string, Icon> _cache = new();

    /// <summary>
    /// Gets or creates a cached icon for the given status color and aging factor.
    /// </summary>
    public Icon GetOrCreate(byte r, byte g, byte b, double agingFactor)
    {
        var key = $"{r}-{g}-{b}-{agingFactor:F2}";

        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var icon = CircleIconRenderer.CreateCircleIcon(r, g, b, agingFactor);
        _cache[key] = icon;
        return icon;
    }

    /// <summary>
    /// Clears all cached icons, disposing each one.
    /// </summary>
    public void Clear()
    {
        foreach (var icon in _cache.Values)
        {
            icon.Dispose();
        }

        _cache.Clear();
    }

    public void Dispose()
    {
        Clear();
    }
}
