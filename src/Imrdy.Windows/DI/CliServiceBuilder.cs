using Imrdy.Core;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace Imrdy.Windows.DI;

/// <summary>
/// DI composition for CLI management commands.
/// Registers Core services + validators + Spectre IAnsiConsole. No WinForms.
/// </summary>
public static class CliServiceBuilder
{
    public static ServiceProvider Build(bool verbose = false, bool quiet = false)
    {
        var services = new ServiceCollection();
        services.AddCoreServices();
        services.AddSerilog(verbose: verbose, quiet: quiet);
        services.AddSingleton(AnsiConsole.Console);
        return services.BuildServiceProvider();
    }
}
