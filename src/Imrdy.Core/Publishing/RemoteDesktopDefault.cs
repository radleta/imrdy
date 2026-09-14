using Imrdy.Core.State;

namespace Imrdy.Core.Publishing;

/// <summary>
/// Picks the local desktop a newly seen remote session defaults to when its publisher has no
/// desktop mapping.
/// </summary>
public static class RemoteDesktopDefault
{
    /// <summary>
    /// The desktop of the most recently active other session from <paramref name="originMachine"/>
    /// that has one, falling back to <paramref name="currentDesktop"/>. Sibling desktops live on
    /// the session files, so the choice survives a tray restart, where the active desktop is often
    /// not the one that machine's sessions were assigned to.
    /// </summary>
    public static int? Resolve(
        string originMachine,
        string sessionId,
        IEnumerable<StateFileModel> knownSessions,
        int? currentDesktop)
        => knownSessions
               .Where(s => s.DesktopIndex.HasValue
                           && s.SessionId != sessionId
                           && string.Equals(s.OriginMachine, originMachine, StringComparison.OrdinalIgnoreCase))
               .MaxBy(s => s.Timestamp)?.DesktopIndex
           ?? currentDesktop;
}
