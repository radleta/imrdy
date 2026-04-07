using Imrdy.Core;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace Imrdy.Windows.Commands;

/// <summary>
/// Signals the running tray app to exit via a named EventWaitHandle.
/// The next hook call will auto-spawn a fresh instance.
/// </summary>
internal static class StopCommand
{
    public static int Run(ServiceProvider services)
    {
        var console = services.GetRequiredService<IAnsiConsole>();

        try
        {
            if (!EventWaitHandle.TryOpenExisting(ImrdyPaths.StopEventName, out var handle))
            {
                console.MarkupLine("[yellow]No running instance found.[/]");
                return 1;
            }

            using (handle)
            {
                handle.Set();
            }

            console.MarkupLine("[green]Stop signal sent.[/]");
            return 0;
        }
        catch (Exception ex)
        {
            console.MarkupLine($"[red]Failed to stop:[/] {Markup.Escape(ex.Message)}");
            return 2;
        }
    }
}
