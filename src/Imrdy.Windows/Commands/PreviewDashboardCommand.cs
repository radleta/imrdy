using System.Text.Json;
using Imrdy.Core;
using Imrdy.Core.Display;
using Imrdy.Windows.Dashboard;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Imrdy.Windows.Commands;

/// <summary>
/// Standalone preview harness for <see cref="SessionDashboardForm"/>.
/// Deserializes a <see cref="DashboardViewModel"/> fixture JSON and runs the form
/// in a normal WinForms message loop.
///
/// Design notes:
/// - Intentionally bypasses the Global\ImrdyMonitor mutex — preview is a dev tool
///   that must be invocable while the real tray is running.
/// - Builds an inline ServiceCollection (no CliServiceBuilder / MonitorServiceBuilder).
///   SessionDashboardForm's real child controls draw themselves via GDI+ and the shared
///   ImrdyPalette, so ILoggerFactory is the only service it resolves — no renderer or
///   graphics-pack registration is needed here.
/// - Uses source-generated JSON (ImrdyJsonContext) for trim-safe deserialization.
/// </summary>
internal static class PreviewDashboardCommand
{
    public static int Run(string[] args)
    {
        // Validate args: expect "preview-dashboard <path>"
        if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
        {
            Console.Error.WriteLine("Usage: imrdy preview-dashboard <fixture.json>");
            return 1;
        }

        var fixturePath = args[1];

        if (!File.Exists(fixturePath))
        {
            Console.Error.WriteLine($"Error: fixture file not found: {fixturePath}");
            return 1;
        }

        // Build a minimal inline ServiceCollection before parsing so AddSerilog configures
        // Log.Logger (static) before any catch block may need it.
        // SessionDashboardForm only needs ILoggerFactory (ArgumentNullException.ThrowIfNull in ctor);
        // its child controls render via GDI+/ImrdyPalette rather than the icon renderers.
        var services = new ServiceCollection();
        services.AddSerilog(verbose: false, quiet: false);

        // Deserialize via source-generated context (no reflection, trim-safe).
        DashboardViewModel? viewModel;
        try
        {
            var bytes = File.ReadAllBytes(fixturePath);
            viewModel = JsonSerializer.Deserialize(bytes, ImrdyJsonContext.Default.DashboardViewModel);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "failed to parse fixture {Path}", fixturePath);
            Console.Error.WriteLine($"Error: failed to parse fixture '{fixturePath}': {ex.Message}");
            return 1;
        }

        if (viewModel is null)
        {
            Console.Error.WriteLine($"Error: fixture deserialized to null — check JSON structure: {fixturePath}");
            return 1;
        }

        using var sp = services.BuildServiceProvider();
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

        // isPinned: true  — initializes Opacity = 1.0 immediately (no HoverDashboardController).
        // isPreviewMode: true — Escape calls Application.Exit() instead of Unpin()+Hide().
        // StartPosition = CenterScreen — set after construction so WinForms centers on
        //   the primary monitor before Application.Run opens the message loop.
        var form = new SessionDashboardForm(viewModel, desktopManager: null, loggerFactory, isPinned: true, isPreviewMode: true);
        form.StartPosition = FormStartPosition.CenterScreen;
        Application.Run(form);
        return 0;
    }
}
