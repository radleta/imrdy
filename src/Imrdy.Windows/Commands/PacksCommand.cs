using System.Text.Json;
using Imrdy.Core;
using Imrdy.Core.Sound;
using Imrdy.Core.Validation;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace Imrdy.Windows.Commands;

/// <summary>
/// Manages sound packs: list, test, validate, set-default.
/// </summary>
internal static class PacksCommand
{
    private static readonly string PacksRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "sounds", "packs");

    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "sounds", "config.json");

    public static int Run(ServiceProvider services, string[] args, bool json)
    {
        if (args.Length == 0)
        {
            return List(services, json);
        }

        return args[0] switch
        {
            "list" => List(services, json),
            "test" => Test(services, args[1..]),
            "validate" => Validate(services, args[1..], json),
            "set-default" => SetDefault(services, args[1..]),
            _ => UnknownSubcommand(services, args[0]),
        };
    }

    private static int List(ServiceProvider services, bool json)
    {
        var packLoader = services.GetRequiredService<PackLoader>();
        var console = services.GetRequiredService<IAnsiConsole>();

        try
        {
            var packs = packLoader.LoadPacks(PacksRoot);

            if (json)
            {
                var output = packs.Select(p => new
                {
                    name = p.Name,
                    description = p.Description,
                    version = p.Version,
                    directory = p.PackDirectory,
                    events = p.WavFiles.ToDictionary(
                        kv => kv.Key.ToString(),
                        kv => kv.Value.Length),
                });
                Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }

            if (packs.Count == 0)
            {
                console.MarkupLine("[dim]No sound packs installed.[/]");
                console.MarkupLine($"[dim]Pack directory: {Markup.Escape(PacksRoot)}[/]");
                return 0;
            }

            var table = new Table();
            table.AddColumn("Pack");
            table.AddColumn("Version");
            table.AddColumn("Events");
            table.AddColumn("Clips");
            table.AddColumn("Description");

            foreach (var p in packs.OrderBy(p => p.Name))
            {
                table.AddRow(
                    Markup.Escape(p.Name),
                    Markup.Escape(p.Version),
                    p.WavFiles.Count.ToString(),
                    p.WavFiles.Values.Sum(v => v.Length).ToString(),
                    Markup.Escape(p.Description));
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

    private static int Test(ServiceProvider services, string[] args)
    {
        var packLoader = services.GetRequiredService<PackLoader>();
        var console = services.GetRequiredService<IAnsiConsole>();

        if (args.Length == 0)
        {
            console.MarkupLine("[red]Usage:[/] imrdy packs test <name> [event]");
            return 1;
        }

        var packName = args[0];
        var eventName = args.Length > 1 ? args[1] : null;

        try
        {
            var packs = packLoader.LoadPacks(PacksRoot);
            var pack = packs.FirstOrDefault(p =>
                string.Equals(p.Name, packName, StringComparison.OrdinalIgnoreCase));

            if (pack is null)
            {
                console.MarkupLine($"[red]Pack not found:[/] {Markup.Escape(packName)}");
                return 1;
            }

            // Select event
            KeyValuePair<SoundEvent, string[]> selected;
            if (eventName is not null)
            {
                var soundEvent = SoundEventExtensions.FromFolderName(eventName);
                if (soundEvent is null || !pack.WavFiles.ContainsKey(soundEvent.Value))
                {
                    console.MarkupLine($"[red]Event not found:[/] {Markup.Escape(eventName)}");
                    console.MarkupLine("[dim]Available events:[/] " +
                        string.Join(", ", pack.WavFiles.Keys.Select(k => k.ToString())));
                    return 1;
                }

                selected = new KeyValuePair<SoundEvent, string[]>(soundEvent.Value, pack.WavFiles[soundEvent.Value]);
            }
            else
            {
                // Pick a random event
                if (pack.WavFiles.Count == 0)
                {
                    console.MarkupLine("[red]Pack has no WAV files.[/]");
                    return 1;
                }

                selected = pack.WavFiles.ElementAt(Random.Shared.Next(pack.WavFiles.Count));
            }

            // Pick a random WAV from the event
            var wavPath = selected.Value[Random.Shared.Next(selected.Value.Length)];
            console.MarkupLine($"Playing [green]{Markup.Escape(selected.Key.ToString())}[/] " +
                $"from [bold]{Markup.Escape(pack.Name)}[/]...");
            console.MarkupLine($"[dim]{Markup.Escape(Path.GetFileName(wavPath))}[/]");

            // PlaySync so process stays alive until playback finishes.
            // ISoundPlayer uses async Play() which would exit immediately.
            using var player = new System.Media.SoundPlayer(wavPath);
            player.PlaySync();
            return 0;
        }
        catch (Exception ex)
        {
            console.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return 2;
        }
    }

    private static int Validate(ServiceProvider services, string[] args, bool json)
    {
        var packLoader = services.GetRequiredService<PackLoader>();
        var packValidator = services.GetRequiredService<PackValidator>();
        var console = services.GetRequiredService<IAnsiConsole>();

        try
        {
            // Validate specific pack or all
            var packDirs = new List<string>();
            if (args.Length > 0)
            {
                var specificDir = Path.Combine(PacksRoot, args[0]);
                if (!Directory.Exists(specificDir))
                {
                    console.MarkupLine($"[red]Pack directory not found:[/] {Markup.Escape(args[0])}");
                    return 1;
                }

                packDirs.Add(specificDir);
            }
            else if (Directory.Exists(PacksRoot))
            {
                packDirs.AddRange(Directory.GetDirectories(PacksRoot));
            }

            if (packDirs.Count == 0)
            {
                console.MarkupLine("[dim]No packs to validate.[/]");
                return 0;
            }

            var allValid = true;
            var results = new List<object>();

            foreach (var packDir in packDirs)
            {
                var result = packValidator.Validate(packDir);
                var packName = Path.GetFileName(packDir);

                if (json)
                {
                    results.Add(new
                    {
                        name = packName,
                        valid = result.IsValid,
                        errors = result.Errors.Select(e => new
                        {
                            path = e.Path,
                            message = e.Message,
                            severity = e.Severity.ToString().ToLowerInvariant(),
                        }),
                    });
                }
                else
                {
                    var tree = new Tree(result.IsValid
                        ? $"[green]\u2713[/] {Markup.Escape(packName)}"
                        : $"[red]\u2717[/] {Markup.Escape(packName)}");

                    foreach (var error in result.Errors)
                    {
                        var color = error.Severity == ValidationSeverity.Error ? "red" : "yellow";
                        tree.AddNode($"[{color}]{Markup.Escape(error.Message)}[/]");
                    }

                    if (result.Errors.Count == 0)
                    {
                        tree.AddNode("[green]All checks passed[/]");
                    }

                    console.Write(tree);
                }

                if (!result.IsValid)
                {
                    allValid = false;
                }
            }

            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            }

            return allValid ? 0 : 1;
        }
        catch (Exception ex)
        {
            console.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return 2;
        }
    }

    private static int SetDefault(ServiceProvider services, string[] args)
    {
        var packLoader = services.GetRequiredService<PackLoader>();
        var console = services.GetRequiredService<IAnsiConsole>();

        if (args.Length == 0)
        {
            console.MarkupLine("[red]Usage:[/] imrdy packs set-default <name>");
            return 1;
        }

        var packName = args[0];

        try
        {
            // Verify pack exists
            var packs = packLoader.LoadPacks(PacksRoot);
            var pack = packs.FirstOrDefault(p =>
                string.Equals(p.Name, packName, StringComparison.OrdinalIgnoreCase));

            if (pack is null)
            {
                console.MarkupLine($"[red]Pack not found:[/] {Markup.Escape(packName)}");
                return 1;
            }

            // Read existing config or create new
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

            // Update default
            config = config with { Default = pack.Name };

            SoundConfigWriter.Save(config, ConfigPath);

            console.MarkupLine($"Default pack set to [green]{Markup.Escape(pack.Name)}[/]");
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
        console.MarkupLine("Run [dim]imrdy packs --help[/] for usage.");
        return 1;
    }
}
