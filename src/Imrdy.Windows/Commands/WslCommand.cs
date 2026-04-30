using System.Text.Json.Nodes;
using Imrdy.Core;
using Imrdy.Core.State;
using Imrdy.Core.Wsl;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace Imrdy.Windows.Commands;

internal static class WslCommand
{

    public static int Run(ServiceProvider services, string[] args, bool json)
    {
        if (args.Length > 0 && args[0] is "--help" or "-h" or "help")
            return Help(services.GetRequiredService<IAnsiConsole>());

        if (args.Length == 0)
            return List(services, json);

        return args[0] switch
        {
            "list" => List(services, json),
            "add" => Add(services, args[1..], json),
            "remove" => Remove(services, args[1..], json),
            _ => UnknownSubcommand(services, args[0]),
        };
    }

    private static int Help(IAnsiConsole console)
    {
        console.WriteLine("Usage: imrdy wsl <subcommand> [options]");
        console.WriteLine("");
        console.WriteLine("Subcommands:");
        console.WriteLine("  list                          List configured WSL distros (default)");
        console.WriteLine("  add <distro> [--linux-home P] Add or update a distro entry");
        console.WriteLine("  remove <distro>               Remove a distro entry");
        console.WriteLine("");
        console.WriteLine("Options:");
        console.WriteLine("  --json   Emit machine-readable JSON output");
        console.WriteLine("  --help   Show this help");
        console.WriteLine("");
        console.WriteLine("Examples:");
        console.WriteLine("  imrdy wsl list");
        console.WriteLine("  imrdy wsl list --json");
        console.WriteLine("  imrdy wsl add Ubuntu-22.04");
        console.WriteLine("  imrdy wsl add Ubuntu-22.04 --linux-home /home/alice");
        console.WriteLine("  imrdy wsl remove Ubuntu-22.04");
        return 0;
    }

    private static int List(ServiceProvider services, bool json)
    {
        var wslDistroStore = services.GetRequiredService<WslDistroStore>();
        var stateReader = services.GetRequiredService<StateFileReader>();
        var console = services.GetRequiredService<IAnsiConsole>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(WslCommand));

        try
        {
            var config = wslDistroStore.Load();
            var distros = config.Distros ?? [];

            // Determine which distros are currently running via wsl.exe.
            var runningNames = GetRunningDistroNames(logger);

            // Count active sessions for each distro: probe UNC paths for each enabled home.
            var sessionCounts = CountSessionsPerDistro(distros, runningNames, stateReader, logger);

            if (json)
            {
                var output = new JsonArray(distros.Select(d =>
                {
                    var status = runningNames.Contains(d.Name) ? "running" : "stopped";
                    return (JsonNode)new JsonObject
                    {
                        ["name"] = d.Name,
                        ["status"] = status,
                        ["sessions"] = sessionCounts.TryGetValue(d.Name, out var c) ? c : 0,
                        ["enabled"] = d.Enabled,
                    };
                }).ToArray());
                console.WriteLine(output.ToJsonString(ImrdyJsonContext.Indented));
                return 0;
            }

            if (distros.Count == 0)
            {
                console.MarkupLine("[dim]No WSL distros configured.[/]");
                console.MarkupLine("[dim]Use [/][green]imrdy wsl add <distro>[/][dim] to add one.[/]");
                return 0;
            }

            var table = new Table();
            table.AddColumn("Name");
            table.AddColumn("Status");
            table.AddColumn("Sessions");
            table.AddColumn("Watch");

            foreach (var d in distros.OrderBy(d => d.Name))
            {
                var isRunning = runningNames.Contains(d.Name);
                var statusStr = isRunning ? "[green]running[/]" : "[dim]stopped[/]";
                var sessions = sessionCounts.TryGetValue(d.Name, out var sc) ? sc : 0;
                var sessionStr = sessions > 0 ? $"[green]{sessions}[/]" : "[dim]0[/]";
                var watchStr = d.Enabled ? "[green]on[/]" : "[dim]off[/]";

                table.AddRow(
                    Markup.Escape(d.Name),
                    statusStr,
                    sessionStr,
                    watchStr);
            }

            console.Write(table);
            return 0;
        }
        catch (Exception ex)
        {
            console.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return 2;
        }
    }

    private static int Add(ServiceProvider services, string[] args, bool json)
    {
        var wslDistroStore = services.GetRequiredService<WslDistroStore>();
        var console = services.GetRequiredService<IAnsiConsole>();

        if (args.Length == 0)
        {
            console.MarkupLine("[red]Usage:[/] imrdy wsl add <distro> [[--linux-home PATH]]");
            return 1;
        }

        var name = args[0];

        // Reject distro names that contain path-separator characters — these would
        // inject extra UNC segments when the name is later used in Path.Combine.
        if (name.IndexOfAny(['\\', '/']) >= 0)
        {
            console.MarkupLine($"[red]Error:[/] Distro name must not contain path separators: {Markup.Escape(name)}");
            return 1;
        }

        string? linuxHome = null;

        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--linux-home" && i + 1 < args.Length)
            {
                linuxHome = args[++i];
            }
        }

        // Reject linux-home values with '..' segments — after TrimStart('/'), a path like
        // /home/alice/../../../Windows would resolve outside the distro UNC root.
        if (linuxHome is not null)
        {
            var segments = linuxHome.TrimStart('/').Split('/', '\\');
            if (Array.Exists(segments, s => s == ".."))
            {
                console.MarkupLine($"[red]Error:[/] --linux-home must not contain '..' segments: {Markup.Escape(linuxHome)}");
                return 1;
            }
        }

        try
        {
            wslDistroStore.Add(name, linuxHome);
            var homeNote = linuxHome is not null ? $" (home: {Markup.Escape(linuxHome)})" : "";

            if (json)
            {
                var obj = new JsonObject
                {
                    ["name"] = name,
                    ["linux_home"] = linuxHome,
                    ["added"] = true,
                };
                console.WriteLine(obj.ToJsonString(ImrdyJsonContext.Indented));
            }
            else
            {
                console.MarkupLine($"Added [green]{Markup.Escape(name)}[/]{homeNote}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            console.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return 2;
        }
    }

    private static int Remove(ServiceProvider services, string[] args, bool json)
    {
        var wslDistroStore = services.GetRequiredService<WslDistroStore>();
        var console = services.GetRequiredService<IAnsiConsole>();

        if (args.Length == 0)
        {
            console.MarkupLine("[red]Usage:[/] imrdy wsl remove <distro>");
            return 1;
        }

        var name = args[0];

        try
        {
            wslDistroStore.Remove(name);

            if (json)
            {
                var obj = new JsonObject
                {
                    ["name"] = name,
                    ["removed"] = true,
                };
                console.WriteLine(obj.ToJsonString(ImrdyJsonContext.Indented));
            }
            else
            {
                console.MarkupLine($"Removed [green]{Markup.Escape(name)}[/]");
            }

            return 0;
        }
        catch (Exception ex)
        {
            console.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return 2;
        }
    }

    private static int UnknownSubcommand(ServiceProvider services, string sub)
    {
        var console = services.GetRequiredService<IAnsiConsole>();
        console.MarkupLine($"[red]Unknown subcommand:[/] {Markup.Escape(sub)}");
        console.MarkupLine("Run [dim]imrdy wsl --help[/] for usage.");
        return 1;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static HashSet<string> GetRunningDistroNames(ILogger logger)
    {
        try
        {
            var discovered = WslDistroDiscovery.DiscoverAsync(CancellationToken.None, logger)
                .GetAwaiter().GetResult();
            return new HashSet<string>(discovered.Select(d => d.Name), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "WSL running-distro probe failed");
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static Dictionary<string, int> CountSessionsPerDistro(
        List<WslDistroEntry> distros,
        HashSet<string> runningNames,
        StateFileReader stateReader,
        ILogger logger)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var d in distros)
        {
            var total = 0;

            if (runningNames.Contains(d.Name) && d.LinuxHomes is not null)
            {
                foreach (var home in d.LinuxHomes)
                {
                    // home is like "/home/alice"; map to \\wsl.localhost\<distro>\home\alice\.imrdy\sessions
                    var linuxRelative = home.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                    var uncSessions = Path.Combine(@"\\wsl.localhost\", d.Name, linuxRelative, ".imrdy", "sessions");

                    if (!Directory.Exists(uncSessions)) continue;
                    try
                    {
                        var sessions = stateReader.ReadAllStateFiles(uncSessions);
                        total += sessions.Count(s =>
                            !string.Equals(s.Status, "end", StringComparison.OrdinalIgnoreCase));
                    }
                    catch (Exception ex)
                    {
                        // UNC traversal failure — skip this home rather than aborting the count.
                        logger.LogDebug(ex, "Session count skipped for distro {Distro} home {Home}", d.Name, home);
                    }
                }
            }

            counts[d.Name] = total;
        }

        return counts;
    }
}
