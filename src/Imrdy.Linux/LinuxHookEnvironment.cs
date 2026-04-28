using Imrdy.Core.Hooks;

namespace Imrdy.Linux;

internal sealed class LinuxHookEnvironment : IHookEnvironment
{
    public int? ResolveTerminalPid(int currentPid, string sessionId) => null;

    public void EnsureTrayRunning() { /* no-op on Linux */ }

    public string NormalizeCwd(string? cwd) => cwd ?? "";

    public void OnSessionEnd(string sessionId) { /* no-op — no PID cache on Linux */ }
}
