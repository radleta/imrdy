using Imrdy.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Imrdy.Windows.DI;

/// <summary>
/// Minimal DI composition for the hook fast path.
/// Registers Core services only — no Spectre, no WinForms.
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
