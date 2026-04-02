using System.Text.Json;
using Imrdy.Core;
using Imrdy.Core.Sound;
using Imrdy.Core.Validation;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Color = Spectre.Console.Color;
using Panel = Spectre.Console.Panel;

namespace Imrdy.Windows.Commands;

/// <summary>
/// Manages configuration: show, set, path, validate.
/// </summary>
internal static class ConfigCommand
{
    private static readonly string TrayDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".imrdy");

    private static readonly string SessionsDir = Path.Combine(TrayDir, "sessions");
    private static readonly string WorkspacesPath = Path.Combine(TrayDir, "workspaces.json");
    private static readonly string LogsDir = Path.Combine(TrayDir, "logs");

    private static readonly string SoundsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "sounds");

    private static readonly string ConfigPath = Path.Combine(SoundsDir, "config.json");
    private static readonly string PacksDir = Path.Combine(SoundsDir, "packs");

    public static int Run(ServiceProvider services, string[] args, bool json)
    {
        if (args.Length == 0)
        {
            return ShowPath(services, json);
        }

        return args[0] switch
        {
            "show" => Show(services, json),
            "set" => Set(services, args[1..]),
            "path" => ShowPath(services, json),
            "validate" => Validate(services, json),
            _ => UnknownSubcommand(services, args[0]),
        };
    }

    private static int Show(ServiceProvider services, bool json)
    {
        var console = services.GetRequiredService<IAnsiConsole>();

        try
        {
            if (!File.Exists(ConfigPath))
            {
                if (json)
                {
                    Console.WriteLine("{}");
                }
                else
                {
                    console.MarkupLine("[dim]No config file found.[/]");
                    console.MarkupLine($"[dim]Expected at: {Markup.Escape(ConfigPath)}[/]");
                }

                return 0;
            }

            var content = File.ReadAllText(ConfigPath);

            if (json)
            {
                Console.WriteLine(content);
                return 0;
            }

            // Pretty-print with Spectre JSON panel
            try
            {
                var doc = JsonDocument.Parse(content);
                var formatted = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
                console.MarkupLine("[bold]Sound Configuration[/]");
                console.MarkupLine($"[dim]{Markup.Escape(ConfigPath)}[/]");
                console.WriteLine();
                console.Write(new Panel(Markup.Escape(formatted))
                    .Header("config.json")
                    .BorderColor(Color.Grey));
            }
            catch (JsonException)
            {
                console.MarkupLine("[yellow]Warning: config.json contains invalid JSON[/]");
                console.WriteLine(content);
            }

            return 0;
        }
        catch (Exception ex)
        {
            console.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return 2;
        }
    }

    private static int Set(ServiceProvider services, string[] args)
    {
        var console = services.GetRequiredService<IAnsiConsole>();

        if (args.Length < 2)
        {
            console.MarkupLine("[red]Usage:[/] imrdy config set <key> <value>");
            console.MarkupLine("[dim]Keys: default, projectMappings.<project>[/]");
            return 1;
        }

        var key = args[0];
        var value = args[1];

        try
        {
            // Read existing config
            SoundConfig config;
            if (File.Exists(ConfigPath))
            {
                try
                {
                    var bytes = File.ReadAllBytes(ConfigPath);
                    config = JsonSerializer.Deserialize(bytes, ImrdyJsonContext.Default.SoundConfig)
                             ?? new SoundConfig();
                }
                catch (Exception ex)
                {
                    console.MarkupLine($"[yellow]Warning: existing config unreadable ({Markup.Escape(ex.Message)}), using defaults[/]");
                    config = new SoundConfig();
                }
            }
            else
            {
                config = new SoundConfig();
            }

            // Apply update
            if (string.Equals(key, "default", StringComparison.OrdinalIgnoreCase))
            {
                config = config with { Default = value };
            }
            else if (key.StartsWith("projectMappings.", StringComparison.OrdinalIgnoreCase) && key.Length > 16)
            {
                var project = key[16..]; // After "projectMappings."
                var mappings = new Dictionary<string, string>(config.ProjectMappings)
                {
                    [project] = value,
                };
                config = config with { ProjectMappings = mappings };
            }
            else
            {
                console.MarkupLine($"[red]Unknown key:[/] {Markup.Escape(key)}");
                console.MarkupLine("[dim]Valid keys: default, projectMappings.<project>[/]");
                return 1;
            }

            // Atomic write
            var dir = Path.GetDirectoryName(ConfigPath);
            if (dir is not null && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var tmpPath = ConfigPath + ".tmp";
            var json = JsonSerializer.SerializeToUtf8Bytes(config, ImrdyJsonContext.Default.SoundConfig);
            File.WriteAllBytes(tmpPath, json);
            File.Move(tmpPath, ConfigPath, overwrite: true);

            console.MarkupLine($"Set [green]{Markup.Escape(key)}[/] = [bold]{Markup.Escape(value)}[/]");
            return 0;
        }
        catch (Exception ex)
        {
            console.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return 2;
        }
    }

    private static int ShowPath(ServiceProvider services, bool json)
    {
        var console = services.GetRequiredService<IAnsiConsole>();

        var paths = new Dictionary<string, string>
        {
            ["sessions"] = SessionsDir,
            ["workspaces"] = WorkspacesPath,
            ["config"] = ConfigPath,
            ["packs"] = PacksDir,
            ["logs"] = LogsDir,
            ["tray_dir"] = TrayDir,
            ["sounds_dir"] = SoundsDir,
        };

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(paths, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        var table = new Table();
        table.AddColumn("Name");
        table.AddColumn("Path");
        table.AddColumn("Exists");

        foreach (var (name, path) in paths.OrderBy(p => p.Key))
        {
            var exists = path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? File.Exists(path)
                : Directory.Exists(path);

            table.AddRow(
                Markup.Escape(name),
                Markup.Escape(path),
                exists ? "[green]\u2713[/]" : "[dim]\u2717[/]");
        }

        console.MarkupLine("[bold]File Paths:[/]");
        console.Write(table);
        return 0;
    }

    private static int Validate(ServiceProvider services, bool json)
    {
        var configValidator = services.GetRequiredService<ConfigValidator>();
        var workspaceValidator = services.GetRequiredService<WorkspaceValidator>();
        var packLoader = services.GetRequiredService<PackLoader>();
        var console = services.GetRequiredService<IAnsiConsole>();

        try
        {
            var packs = packLoader.LoadPacks(PacksDir);
            var packNames = packs.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var configResult = configValidator.Validate(ConfigPath, packNames);
            var workspaceResult = workspaceValidator.Validate(WorkspacesPath);

            if (json)
            {
                var output = new
                {
                    config = new
                    {
                        valid = configResult.IsValid,
                        errors = configResult.Errors.Select(e => new
                        {
                            path = e.Path,
                            message = e.Message,
                            severity = e.Severity.ToString().ToLowerInvariant(),
                        }),
                    },
                    workspaces = new
                    {
                        valid = workspaceResult.IsValid,
                        errors = workspaceResult.Errors.Select(e => new
                        {
                            path = e.Path,
                            message = e.Message,
                            severity = e.Severity.ToString().ToLowerInvariant(),
                        }),
                    },
                };
                Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
                return (configResult.IsValid && workspaceResult.IsValid) ? 0 : 1;
            }

            // Config validation tree
            var configTree = new Tree(configResult.IsValid
                ? "[green]\u2713[/] config.json"
                : "[red]\u2717[/] config.json");

            foreach (var error in configResult.Errors)
            {
                var color = error.Severity == ValidationSeverity.Error ? "red" : "yellow";
                configTree.AddNode($"[{color}]{Markup.Escape(error.Message)}[/]");
            }

            if (configResult.Errors.Count == 0)
            {
                configTree.AddNode("[green]All checks passed[/]");
            }

            console.Write(configTree);

            // Workspace validation tree
            var wsTree = new Tree(workspaceResult.IsValid
                ? "[green]\u2713[/] workspaces.json"
                : "[red]\u2717[/] workspaces.json");

            foreach (var error in workspaceResult.Errors)
            {
                var color = error.Severity == ValidationSeverity.Error ? "red" : "yellow";
                wsTree.AddNode($"[{color}]{Markup.Escape(error.Message)}[/]");
            }

            if (workspaceResult.Errors.Count == 0)
            {
                wsTree.AddNode("[green]All checks passed[/]");
            }

            console.Write(wsTree);

            return (configResult.IsValid && workspaceResult.IsValid) ? 0 : 1;
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
        console.MarkupLine("Run [dim]imrdy config --help[/] for usage.");
        return 1;
    }
}
