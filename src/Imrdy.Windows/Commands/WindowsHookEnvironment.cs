using Imrdy.Core.Desktop;
using Imrdy.Core.Hooks;
using Microsoft.Extensions.Logging;

namespace Imrdy.Windows.Commands;

internal sealed class WindowsHookEnvironment(ILogger logger) : IHookEnvironment
{
    public int? ResolveTerminalPid(int currentPid, string sessionId)
        => ProcessResolver.ResolveTerminalPid(currentPid, sessionId);

    public void EnsureTrayRunning() => TraySpawner.EnsureRunning(logger);

    public string NormalizeCwd(string? cwd) => PathNormalizer.Normalize(cwd ?? "");

    public void OnSessionEnd(string sessionId) => ProcessResolver.ClearSession(sessionId);

    public string? GetWslDistro() => null;
}
