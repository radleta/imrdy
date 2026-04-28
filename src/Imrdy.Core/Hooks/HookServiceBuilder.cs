using Microsoft.Extensions.DependencyInjection;

namespace Imrdy.Core.Hooks;

/// <summary>
/// Minimal DI composition for the hook fast path.
/// Registers Core services only — no Spectre, no WinForms.
/// Used by both Imrdy.Windows and Imrdy.Linux.
/// </summary>
public static class HookServiceBuilder
{
    public static ServiceProvider Build(bool verbose = false, bool quiet = false)
    {
        var services = new ServiceCollection();
        services.AddCoreServices();
        services.AddSerilog(verbose: verbose, quiet: quiet, fileSink: true, logPath: ImrdyPaths.HookLog);
        return services.BuildServiceProvider();
    }
}
