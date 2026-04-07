using Imrdy.Core;
using Imrdy.Windows.Commands;
using Imrdy.Windows.DI;
using Imrdy.Windows.Models;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Imrdy.Windows;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            // Fast path — hook runs hundreds of times, minimal DI, bypass WinForms
            if (args.Length > 0 && args[0] == "hook")
            {
                using var services = HookServiceBuilder.Build();
                return HookCommand.Run(services, Console.In);
            }

            // Management commands — Spectre.Console for rich output
            if (args.Length > 0 && args[0] is "status" or "packs" or "config" or "workspace"
                    or "--help" or "-h" or "--version")
            {
                using var services = CliServiceBuilder.Build();
                return CommandRouter.Route(services, args);
            }

            // Default: start the system tray monitor
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using var mutex = new Mutex(true, ImrdyPaths.MutexName, out bool created);
            if (!created)
            {
                // Already running
                return 0;
            }

            var monitorOptions = MonitorOptions.Parse(args);
            using var monitorServices = MonitorServiceBuilder.Build(monitorOptions);
            var trayApp = monitorServices.GetRequiredService<TrayApp>();
            Application.Run(trayApp);
            GC.KeepAlive(mutex);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            Log.Fatal(ex, "Unhandled exception");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
