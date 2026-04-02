using Imrdy.Core;
using Imrdy.Core.Desktop;
using Imrdy.Core.Sound;
using Imrdy.Windows.Desktop;
using Imrdy.Windows.Sound;
using Microsoft.Extensions.DependencyInjection;

namespace Imrdy.Windows.DI;

/// <summary>
/// DI composition for the system tray monitor.
/// Registers everything: Core services + WinForms UI + ISoundPlayer + IDesktopManager.
/// </summary>
public static class MonitorServiceBuilder
{
    public static ServiceProvider Build(bool verbose = false, bool quiet = false)
    {
        var services = new ServiceCollection();
        services.AddCoreServices();
        services.AddSerilog(verbose: verbose, quiet: quiet, fileSink: true);
        services.AddSingleton<ISoundPlayer, WinFormsSoundPlayer>();
        services.AddSingleton<IDesktopManager, ComVirtualDesktop>();
        services.AddSingleton<TrayApp>();
        return services.BuildServiceProvider();
    }
}
