using System.Drawing;

namespace Imrdy.Windows.Icons;

/// <summary>
/// Renders tray icons for a given status and aging tier.
/// Implementations cache the returned Icon instances and own their lifetime;
/// callers must NOT dispose the returned Icon.
/// </summary>
internal interface ITrayIconRenderer : IDisposable
{
    /// <summary>
    /// Returns an Icon for the given status and aging tier.
    /// Icon is cached — subsequent calls with the same key return the same instance.
    /// </summary>
    /// <param name="status">Status name (e.g., "busy", "idle", "attention"). Unknown statuses should return a fallback icon, not throw.</param>
    /// <param name="ageTier">Aging tier 0-4 from StatusMap.GetAgingTier. 0 = fresh, 4 = oldest.</param>
    Icon GetIcon(string status, int ageTier);
}
