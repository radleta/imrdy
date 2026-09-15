using Imrdy.Core.State;

namespace Imrdy.Core.Publishing;

/// <summary>
/// Picks the local desktop a newly seen remote session defaults to when its publisher has no
/// desktop mapping.
/// </summary>
public static class RemoteDesktopDefault
{
    /// <summary>
    /// A session seen at its own launch takes <paramref name="currentDesktop"/>: the user just
    /// started it, so the desktop they are looking at is where its window is. Any other first
    /// sighting (a tray restart, a reconnect snapshot) takes the desktop of the most recently
    /// active other session from <paramref name="originMachine"/>, since the active desktop then
    /// has nothing to do with that session. Each falls back to the other.
    /// </summary>
    public static int? Resolve(
        string originMachine,
        string sessionId,
        bool launched,
        IEnumerable<StateFileModel> knownSessions,
        int? currentDesktop)
    {
        if (launched && currentDesktop.HasValue)
        {
            return currentDesktop;
        }

        return knownSessions
                   .Where(s => s.DesktopIndex.HasValue
                               && s.SessionId != sessionId
                               && string.Equals(s.OriginMachine, originMachine, StringComparison.OrdinalIgnoreCase))
                   .MaxBy(s => s.Timestamp)?.DesktopIndex
               ?? currentDesktop;
    }
}
