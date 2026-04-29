namespace Imrdy.Core.Hooks;

public interface IHookEnvironment
{
    /// <summary>Resolves the terminal PID for click-to-focus. Returns null on Linux or on failure.</summary>
    int? ResolveTerminalPid(int currentPid, string sessionId);

    /// <summary>Ensures the tray monitor is running. No-op on Linux.</summary>
    void EnsureTrayRunning();

    /// <summary>
    /// Normalizes the cwd path for the platform. Windows implementation calls
    /// PathNormalizer.Normalize (MSYS → Windows path conversion). Linux implementation
    /// returns the path verbatim — Linux cwd values must never be run through MSYS
    /// normalization, which would mangle them (e.g. /home/radle → h:\ome\radle).
    /// </summary>
    string NormalizeCwd(string? cwd);

    /// <summary>
    /// Called at the end of the SessionEnd branch to perform platform-specific cleanup.
    /// Windows implementation calls ProcessResolver.ClearSession(sessionId) to evict the
    /// PID cache entry. Linux implementation is a no-op — ProcessResolver lives in
    /// Imrdy.Windows and cannot be referenced from Imrdy.Core.
    /// </summary>
    void OnSessionEnd(string sessionId);

    /// <summary>
    /// Returns the WSL distro name when the hook is running inside a WSL2 distro,
    /// or <c>null</c> on Windows-native runs. The Linux impl reads
    /// <c>WSL_DISTRO_NAME</c>; the Windows impl returns <c>null</c>.
    /// </summary>
    string? GetWslDistro();
}
