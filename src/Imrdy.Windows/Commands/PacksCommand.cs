using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Imrdy.Core;
using Imrdy.Core.Sound;
using Imrdy.Core.Validation;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace Imrdy.Windows.Commands;

/// <summary>
/// Manages sound packs: list, test, validate, set-default, remove, pack.
/// </summary>
internal static class PacksCommand
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
            "test" => Test(services, args[1..]),
            "validate" => Validate(services, args[1..], json),
            "set-default" => SetDefault(services, args[1..]),
            "remove" => Remove(services, args[1..], json),
            "pack" => Pack(services, args[1..], json),
            _ => UnknownSubcommand(services, args[0]),
        };
    }

    private static int List(ServiceProvider services, bool json)
    {
        var packLoader = services.GetRequiredService<PackLoader>();
        var console = services.GetRequiredService<IAnsiConsole>();

        try
        {
            var packs = packLoader.LoadPacks(ImrdyPaths.PacksDir);

            if (json)
            {
                var output = new JsonArray(packs.Select(p =>
                {
                    var events = new JsonObject();
                    foreach (var kv in p.WavFiles)
                        events[kv.Key.ToString()] = kv.Value.Length;
                    return (JsonNode)new JsonObject
                    {
                        ["name"] = p.Name,
                        ["description"] = p.Description,
                        ["version"] = p.Version,
                        ["directory"] = p.PackDirectory,
                        ["events"] = events,
                    };
                }).ToArray());
                Console.WriteLine(output.ToJsonString(ImrdyJsonContext.Indented));
                return 0;
            }

            if (packs.Count == 0)
            {
                console.MarkupLine("[dim]No sound packs installed.[/]");
                console.MarkupLine($"[dim]Pack directory: {Markup.Escape(ImrdyPaths.PacksDir)}[/]");
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
            var packs = packLoader.LoadPacks(ImrdyPaths.PacksDir);
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
                var specificDir = Path.GetFullPath(Path.Combine(ImrdyPaths.PacksDir, args[0]));
                if (!specificDir.StartsWith(Path.GetFullPath(ImrdyPaths.PacksDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    console.MarkupLine($"[red]Invalid pack name:[/] {Markup.Escape(args[0])}");
                    return 1;
                }

                if (!Directory.Exists(specificDir))
                {
                    console.MarkupLine($"[red]Pack directory not found:[/] {Markup.Escape(args[0])}");
                    return 1;
                }

                packDirs.Add(specificDir);
            }
            else if (Directory.Exists(ImrdyPaths.PacksDir))
            {
                packDirs.AddRange(Directory.GetDirectories(ImrdyPaths.PacksDir));
            }

            if (packDirs.Count == 0)
            {
                console.MarkupLine("[dim]No packs to validate.[/]");
                return 0;
            }

            var allValid = true;
            var jsonResults = json ? new JsonArray() : null;

            foreach (var packDir in packDirs)
            {
                var result = packValidator.Validate(packDir);
                var packName = Path.GetFileName(packDir);

                if (json)
                {
                    jsonResults!.Add(new JsonObject
                    {
                        ["name"] = packName,
                        ["valid"] = result.IsValid,
                        ["errors"] = new JsonArray(result.Errors.Select(e => (JsonNode)new JsonObject
                        {
                            ["path"] = e.Path,
                            ["message"] = e.Message,
                            ["severity"] = e.Severity.ToString().ToLowerInvariant(),
                        }).ToArray()),
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
                Console.WriteLine(jsonResults!.ToJsonString(ImrdyJsonContext.Indented));
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
            var packs = packLoader.LoadPacks(ImrdyPaths.PacksDir);
            var pack = packs.FirstOrDefault(p =>
                string.Equals(p.Name, packName, StringComparison.OrdinalIgnoreCase));

            if (pack is null)
            {
                console.MarkupLine($"[red]Pack not found:[/] {Markup.Escape(packName)}");
                return 1;
            }

            ConfigReader.Update(c => c with
            {
                Sound = c.Sound with { DefaultPack = pack.Name }
            });

            console.MarkupLine($"Default pack set to [green]{Markup.Escape(pack.Name)}[/]");
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
        var console = services.GetRequiredService<IAnsiConsole>();

        if (args.Length == 0)
        {
            console.MarkupLine("[red]Usage:[/] imrdy packs remove <name>");
            return 1;
        }

        var name = args[0];
        var packDir = Path.GetFullPath(Path.Combine(ImrdyPaths.PacksDir, name));

        // Path containment guard — prevent traversal outside packs directory
        if (!packDir.StartsWith(Path.GetFullPath(ImrdyPaths.PacksDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            console.MarkupLine($"[red]Invalid pack name:[/] {Markup.Escape(name)}");
            return 1;
        }

        try
        {
            if (!Directory.Exists(packDir))
            {
                if (json)
                {
                    Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, string> { ["error"] = $"Pack not found: {name}" }, ImrdyJsonContext.Indented));
                }
                else
                {
                    console.MarkupLine($"[red]Pack not found:[/] {Markup.Escape(name)}");
                }

                return 1;
            }

            Directory.Delete(packDir, recursive: true);

            var defaultCleared = false;
            try
            {
                var config = ConfigReader.Read();
                if (string.Equals(config.Sound.DefaultPack, name, StringComparison.OrdinalIgnoreCase))
                {
                    ConfigReader.Update(c => c with
                    {
                        Sound = c.Sound with { DefaultPack = "random" }
                    });
                    defaultCleared = true;
                }
            }
            catch (Exception ex)
            {
                console.MarkupLine($"[yellow]Warning: could not update config ({Markup.Escape(ex.Message)})[/]");
            }

            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, string> { ["removed"] = name }, ImrdyJsonContext.Indented));
            }
            else
            {
                console.MarkupLine($"Removed pack [green]{Markup.Escape(name)}[/]");
                if (defaultCleared)
                {
                    console.MarkupLine("[yellow]Warning: default pack was cleared (was set to this pack)[/]");
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, string> { ["error"] = ex.Message }, ImrdyJsonContext.Indented));
            }
            else
            {
                console.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            }

            return 2;
        }
    }

    private static int Pack(ServiceProvider services, string[] args, bool json)
    {
        var packValidator = services.GetRequiredService<PackValidator>();
        var console = services.GetRequiredService<IAnsiConsole>();

        if (args.Length == 0)
        {
            console.MarkupLine("[red]Usage:[/] imrdy packs pack <path> [--output <dir>]");
            return 1;
        }

        var path = args[0];

        // Parse --output <dir>
        var outputDir = Directory.GetCurrentDirectory();
        for (var i = 1; i < args.Length - 1; i++)
        {
            if (args[i] == "--output")
            {
                outputDir = args[i + 1];
                break;
            }
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!Directory.Exists(fullPath))
            {
                console.MarkupLine($"[red]Directory not found:[/] {Markup.Escape(path)}");
                return 1;
            }

            // Read pack.json for naming
            var packJsonPath = Path.Combine(fullPath, "pack.json");
            if (!File.Exists(packJsonPath))
            {
                console.MarkupLine($"[red]pack.json not found in:[/] {Markup.Escape(path)}");
                return 1;
            }

            PackJson? packJson;
            try
            {
                var bytes = File.ReadAllBytes(packJsonPath);
                packJson = JsonSerializer.Deserialize(bytes, ImrdyJsonContext.Default.PackJson);
            }
            catch (JsonException ex)
            {
                console.MarkupLine($"[red]Invalid pack.json:[/] {Markup.Escape(ex.Message)}");
                return 1;
            }

            if (packJson is null || string.IsNullOrEmpty(packJson.Name))
            {
                console.MarkupLine("[red]pack.json is missing the 'name' field.[/]");
                return 1;
            }

            // Validate pack structure
            var validationResult = packValidator.Validate(fullPath);
            if (!validationResult.IsValid)
            {
                if (json)
                {
                    var validationOutput = new JsonObject
                    {
                        ["error"] = "Validation failed",
                        ["errors"] = new JsonArray(validationResult.Errors.Select(e => (JsonNode)new JsonObject
                        {
                            ["path"] = e.Path,
                            ["message"] = e.Message,
                            ["severity"] = e.Severity.ToString().ToLowerInvariant(),
                        }).ToArray()),
                    };
                    Console.WriteLine(validationOutput.ToJsonString(ImrdyJsonContext.Indented));
                }
                else
                {
                    var tree = new Tree($"[red]\u2717[/] Validation failed for {Markup.Escape(packJson.Name)}");
                    foreach (var error in validationResult.Errors)
                    {
                        var color = error.Severity == ValidationSeverity.Error ? "red" : "yellow";
                        tree.AddNode($"[{color}]{Markup.Escape(error.Message)}[/]");
                    }

                    console.Write(tree);
                }

                return 1;
            }

            // Create ZIP
            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // Sanitize name/version to prevent path separators in filename
            var safeName = string.Concat(packJson.Name.Split(Path.GetInvalidFileNameChars()));
            var safeVersion = string.Concat(packJson.Version.Split(Path.GetInvalidFileNameChars()));
            var zipFileName = $"pack-{safeName}-v{safeVersion}.zip";
            var zipPath = Path.GetFullPath(Path.Combine(outputDir, zipFileName));

            // Path containment guard — ensure ZIP stays within output directory
            if (!zipPath.StartsWith(Path.GetFullPath(outputDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Path.GetDirectoryName(zipPath), Path.GetFullPath(outputDir), StringComparison.OrdinalIgnoreCase))
            {
                console.MarkupLine("[red]Invalid pack name or version for filename construction.[/]");
                return 1;
            }

            // Remove existing ZIP if present to avoid ZipFile error
            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            ZipFile.CreateFromDirectory(fullPath, zipPath);

            // Compute SHA256
            var sha256Hash = ComputeSha256(zipPath);
            var fileSize = new FileInfo(zipPath).Length;

            if (json)
            {
                var output = new JsonObject
                {
                    ["path"] = zipPath,
                    ["sha256"] = sha256Hash,
                    ["size"] = fileSize,
                };
                Console.WriteLine(output.ToJsonString(ImrdyJsonContext.Indented));
            }
            else
            {
                console.MarkupLine($"Created [green]{Markup.Escape(zipPath)}[/] ({FormatSize(fileSize)})");
                console.MarkupLine($"SHA256: [dim]{sha256Hash}[/]");
            }

            return 0;
        }
        catch (Exception ex)
        {
            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, string> { ["error"] = ex.Message }, ImrdyJsonContext.Indented));
            }
            else
            {
                console.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            }

            return 2;
        }
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexStringLower(hash);
    }

    private static string FormatSize(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            _ => $"{bytes / (1024.0 * 1024.0):F1} MB",
        };
    }

    private static int UnknownSubcommand(ServiceProvider services, string sub)
    {
        var console = services.GetRequiredService<IAnsiConsole>();
        console.MarkupLine($"[red]Unknown subcommand:[/] {Markup.Escape(sub)}");
        console.MarkupLine("Run [dim]imrdy packs --help[/] for usage.");
        return 1;
    }
}
