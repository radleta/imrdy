using System.Text.Json;
using System.Text.Json.Nodes;
using Imrdy.Core;
using Imrdy.Core.Desktop;
using Imrdy.Core.State;
using Imrdy.Core.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace Imrdy.Windows.Commands;

/// <summary>
/// Manages pinned workspaces: list, pin, unpin.
/// </summary>
internal static class WorkspaceCommand
{

    public static int Run(ServiceProvider services, string[] args, bool json)
    {
        if (args.Length == 0)
        {
            return List(services, json);
        }

        return args[0] switch
        {
            "list" => List(services, json),
            "pin" => Pin(services, args[1..]),
            "unpin" => Unpin(services, args[1..]),
            _ => UnknownSubcommand(services, args[0]),
        };
    }

    private static int List(ServiceProvider services, bool json)
    {
        var workspaceStore = services.GetRequiredService<WorkspaceStore>();
        var stateReader = services.GetRequiredService<StateFileReader>();
        var console = services.GetRequiredService<IAnsiConsole>();

        try
        {
            var config = workspaceStore.Load();
            var sessions = Directory.Exists(ImrdyPaths.Sessions)
                ? stateReader.ReadAllStateFiles(ImrdyPaths.Sessions)
                : [];

            if (json)
            {
                var output = new JsonArray(config.Workspaces.Select(w =>
                {
                    var activeSessions = CountActiveSessions(w, sessions);
                    return (JsonNode)new JsonObject
                    {
                        ["name"] = w.Name,
                        ["path"] = w.Path,
                        ["desktop"] = w.Desktop,
                        ["active_sessions"] = activeSessions,
                    };
                }).ToArray());
                Console.WriteLine(output.ToJsonString(ImrdyJsonContext.Indented));
                return 0;
            }

            if (config.Workspaces.Count == 0)
            {
                console.MarkupLine("[dim]No pinned workspaces.[/]");
                console.MarkupLine("[dim]Use [/][green]imrdy workspace pin <path>[/][dim] to add one.[/]");
                return 0;
            }

            var table = new Table();
            table.AddColumn("Name");
            table.AddColumn("Path");
            table.AddColumn("Desktop");
            table.AddColumn("Sessions");

            foreach (var w in config.Workspaces.OrderBy(w => w.Name))
            {
                var activeSessions = CountActiveSessions(w, sessions);
                var sessionStr = activeSessions > 0
                    ? $"[green]{activeSessions}[/]"
                    : "[dim]0[/]";

                table.AddRow(
                    Markup.Escape(w.Name),
                    Markup.Escape(w.Path),
                    w.Desktop.ToString(),
                    sessionStr);
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

    private static int Pin(ServiceProvider services, string[] args)
    {
        var workspaceStore = services.GetRequiredService<WorkspaceStore>();
        var console = services.GetRequiredService<IAnsiConsole>();

        if (args.Length == 0)
        {
            console.MarkupLine("[red]Usage:[/] imrdy workspace pin <path> [--name N] [--desktop D]");
            return 1;
        }

        var path = args[0];
        string? name = null;
        var desktop = 0;

        // Parse optional flags
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--name" && i + 1 < args.Length)
            {
                name = args[++i];
            }
            else if (args[i] == "--desktop" && i + 1 < args.Length)
            {
                if (!int.TryParse(args[++i], out desktop))
                {
                    console.MarkupLine("[red]Invalid desktop number.[/]");
                    return 1;
                }
            }
        }

        // Auto-derive name from path basename if not specified
        name ??= PathNormalizer.DeriveProject(path);
        if (string.IsNullOrEmpty(name))
        {
            name = "unnamed";
        }

        try
        {
            workspaceStore.Pin(path, name, desktop);
            console.MarkupLine($"Pinned [green]{Markup.Escape(name)}[/] at {Markup.Escape(PathNormalizer.Normalize(path))}");
            return 0;
        }
        catch (Exception ex)
        {
            console.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return 2;
        }
    }

    private static int Unpin(ServiceProvider services, string[] args)
    {
        var workspaceStore = services.GetRequiredService<WorkspaceStore>();
        var console = services.GetRequiredService<IAnsiConsole>();

        if (args.Length == 0)
        {
            console.MarkupLine("[red]Usage:[/] imrdy workspace unpin <path>");
            return 1;
        }

        var path = args[0];

        try
        {
            // Check if it exists first for user feedback
            var config = workspaceStore.Load();
            var exists = config.Workspaces.Any(w => PathNormalizer.AreEqual(w.Path, path));

            if (!exists)
            {
                console.MarkupLine($"[yellow]Workspace not found:[/] {Markup.Escape(path)}");
                return 1;
            }

            workspaceStore.Unpin(path);
            console.MarkupLine($"Unpinned [green]{Markup.Escape(PathNormalizer.Normalize(path))}[/]");
            return 0;
        }
        catch (Exception ex)
        {
            console.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return 2;
        }
    }

    private static int CountActiveSessions(WorkspaceEntry workspace, IReadOnlyList<StateFileModel> sessions)
    {
        return sessions.Count(s =>
            !string.Equals(s.Status, "end", StringComparison.OrdinalIgnoreCase) &&
            PathNormalizer.AreEqual(s.Cwd, workspace.Path));
    }

    private static int UnknownSubcommand(ServiceProvider services, string sub)
    {
        var console = services.GetRequiredService<IAnsiConsole>();
        console.MarkupLine($"[red]Unknown subcommand:[/] {Markup.Escape(sub)}");
        console.MarkupLine("Run [dim]imrdy workspace --help[/] for usage.");
        return 1;
    }
}
