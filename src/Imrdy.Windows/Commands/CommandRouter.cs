using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace Imrdy.Windows.Commands;

/// <summary>
/// Routes CLI subcommands to their handlers.
/// Handles --help, --version, and --json global flags.
/// </summary>
internal static class CommandRouter
{
    public static int Route(ServiceProvider services, string[] args)
    {
        if (args.Length == 0)
        {
            return ShowHelp(services);
        }

        var command = args[0];

        // Global flags that don't need subcommand parsing
        if (command is "--help" or "-h")
        {
            return ShowHelp(services);
        }

        if (command == "--version")
        {
            return ShowVersion(services);
        }

        var subArgs = args[1..];
        var json = subArgs.Any(a => a == "--json");
        var help = subArgs.Any(a => a is "--help" or "-h");

        // Strip flags from subArgs for subcommand parsing
        var cleanArgs = subArgs.Where(a => a is not ("--json" or "--help" or "-h")).ToArray();

        if (help)
        {
            return ShowCommandHelp(services, command);
        }

        return command switch
        {
            "status" => StatusCommand.Run(services, json),
            "packs" => PacksCommand.Run(services, cleanArgs, json),
            "config" => ConfigCommand.Run(services, cleanArgs, json),
            "workspace" => WorkspaceCommand.Run(services, cleanArgs, json),
            _ => ShowHelp(services),
        };
    }

    private static int ShowHelp(ServiceProvider services)
    {
        var console = services.GetRequiredService<IAnsiConsole>();
        console.MarkupLine("[bold]imrdy[/] — System tray monitor for Claude Code sessions");
        console.WriteLine();
        console.MarkupLine("[bold]Commands:[/]");
        console.MarkupLine("  [green]status[/]      Show active sessions and workspaces");
        console.MarkupLine("  [green]packs[/]       Manage sound packs");
        console.MarkupLine("  [green]config[/]      Manage configuration");
        console.MarkupLine("  [green]workspace[/]   Manage pinned workspaces");
        console.WriteLine();
        console.MarkupLine("[bold]Global Flags:[/]");
        console.MarkupLine("  [dim]--json[/]       Output as JSON");
        console.MarkupLine("  [dim]--help, -h[/]   Show help");
        console.MarkupLine("  [dim]--version[/]    Show version");
        return 0;
    }

    private static int ShowVersion(ServiceProvider services)
    {
        var console = services.GetRequiredService<IAnsiConsole>();
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "dev";
        console.MarkupLine($"imrdy {Markup.Escape(version)}");
        return 0;
    }

    private static int ShowCommandHelp(ServiceProvider services, string command)
    {
        var console = services.GetRequiredService<IAnsiConsole>();
        switch (command)
        {
            case "status":
                console.MarkupLine("[bold]imrdy status[/]");
                console.MarkupLine("  Show active sessions and workspaces.");
                console.MarkupLine("  [dim]--json[/]  Output as JSON array");
                break;
            case "packs":
                console.MarkupLine("[bold]imrdy packs[/] <subcommand>");
                console.MarkupLine("  [green]list[/]               List installed sound packs");
                console.MarkupLine("  [green]test[/] <name> [event] Play a random sound from a pack");
                console.MarkupLine("  [green]validate[/] [name]    Validate pack structure");
                console.MarkupLine("  [green]set-default[/] <name> Set default sound pack");
                console.MarkupLine("  [green]remove[/] <name>      Remove an installed pack");
                console.MarkupLine("  [green]pack[/] <path>        Validate and package a pack as ZIP");
                break;
            case "config":
                console.MarkupLine("[bold]imrdy config[/] <subcommand>");
                console.MarkupLine("  [green]show[/]              Show current configuration");
                console.MarkupLine("  [green]set[/] <key> <value> Update a config value");
                console.MarkupLine("  [green]path[/]              Show all file paths");
                console.MarkupLine("  [green]validate[/]          Validate configuration files");
                break;
            case "workspace":
                console.MarkupLine("[bold]imrdy workspace[/] <subcommand>");
                console.MarkupLine("  [green]list[/]              List pinned workspaces");
                console.MarkupLine("  [green]pin[/] <path> [--name N] [--desktop D]  Pin a workspace");
                console.MarkupLine("  [green]unpin[/] <path>      Unpin a workspace");
                break;
            default:
                return ShowHelp(services);
        }

        return 0;
    }
}
