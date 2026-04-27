using Imrdy.Core;
using Imrdy.Core.Rendering;
using Imrdy.Windows.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Imrdy.Windows.Commands;

/// <summary>
/// Entry point for <c>imrdy render</c>. Dispatches to sub-paths based on args.
/// Does not wire into <c>Program.cs</c> until step 07.
/// </summary>
internal static class RenderCommand
{
    private const int ExitSuccess = 0;
    private const int ExitUserError = 1;
    private const int ExitRenderError = 2;
    private const int ExitCancelled = 130;

    // Set by Console.CancelKeyPress handler; read between loop iterations.
    private static volatile bool _cancelled = false;

    // Guards against re-subscribing if Run is called more than once (e.g., in tests).
    private static volatile bool _handlerWired = false;

    public static int Run(string[] args)
    {
        // Reset cancellation state so a re-entry (e.g., multiple test invocations) works.
        _cancelled = false;

        if (!_handlerWired)
        {
            _handlerWired = true;
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;   // prevent immediate process termination
                _cancelled = true; // checked between fixtures
                Console.Error.WriteLine("render: cancelling after current fixture...");
            };
        }

        try
        {
            // args[0] is "render" (the verb name); subsequent args are the render sub-args.
            if (args.Length <= 1 || args[1] == "--help" || args[1] == "-h")
            {
                PrintHelp();
                return ExitSuccess;
            }

            if (args[1] == "--list")
            {
                PrintList();
                return ExitSuccess;
            }

            if (args[1] == "--all")
            {
                // Global --all: render every component that has a DefaultFixtureDir
                string? globalOutputDir = null;
                for (int i = 2; i < args.Length; i++)
                {
                    if (args[i] == "--output-dir")
                    {
                        if (i + 1 >= args.Length)
                        {
                            Console.Error.WriteLine("render: --output-dir requires a directory argument.");
                            return ExitUserError;
                        }
                        globalOutputDir = Path.GetFullPath(args[++i]);
                    }
                    else
                    {
                        Console.Error.WriteLine($"render: unknown flag '{args[i]}'.");
                        return ExitUserError;
                    }
                }

                if (_cancelled)
                    return ExitCancelled;

                var globalRepoRoot = ReadDevBuildMarker();
                using var globalSp = new ServiceCollection()
                    .AddSerilog(verbose: false, quiet: true)
                    .BuildServiceProvider();
                var globalLoggerFactory = globalSp.GetRequiredService<ILoggerFactory>();

                bool anyFailure = false;
                foreach (var comp in RenderRegistry.Components.Where(c => c.DefaultFixtureDir is not null))
                {
                    var compOutputDir = globalOutputDir ?? ResolveDefaultOutputDir(comp.Name);
                    int compResult = RunAllForComponent(comp, compOutputDir, globalRepoRoot, globalLoggerFactory);
                    if (compResult == ExitCancelled)
                        return ExitCancelled;
                    if (compResult != ExitSuccess)
                        anyFailure = true;
                }
                return anyFailure ? ExitRenderError : ExitSuccess;
            }

            // args[1] starts with '--' but isn't --help, --list, or --all → unknown flag at top level
            if (args[1].StartsWith("--", StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"render: unknown flag '{args[1]}'. Run 'imrdy render --help' to see options.");
                return ExitUserError;
            }

            // Single-render path: args[1] is a component name
            var component = RenderRegistry.Components.FirstOrDefault(c => c.Name == args[1]);
            if (component is null)
            {
                Console.Error.WriteLine($"render: unknown component '{args[1]}'. Run 'imrdy render --list' to see options.");
                return ExitUserError;
            }

            // Parse args[2..]
            var componentArgs = new List<string>();
            string? explicitOutput = null;
            string? explicitOutputDir = null;
            bool allFlag = false;

            for (int i = 2; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg == "--output")
                {
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("render: --output requires a path argument.");
                        return ExitUserError;
                    }
                    explicitOutput = args[++i];
                }
                else if (arg == "--output-dir")
                {
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("render: --output-dir requires a directory argument.");
                        return ExitUserError;
                    }
                    explicitOutputDir = args[++i];
                }
                else if (arg == "--all")
                {
                    allFlag = true;
                }
                else if (arg.StartsWith("--", StringComparison.Ordinal))
                {
                    Console.Error.WriteLine($"render: unknown flag '{arg}'.");
                    return ExitUserError;
                }
                else
                {
                    componentArgs.Add(arg);
                }
            }

            if (allFlag)
            {
                if (explicitOutput is not null)
                {
                    Console.Error.WriteLine("render: --output cannot combine with --all; use --output-dir.");
                    return ExitUserError;
                }

                if (component.DefaultFixtureDir is null)
                {
                    Console.Error.WriteLine($"render: component '{component.Name}' does not support --all (no default fixture directory).");
                    return ExitUserError;
                }

                if (_cancelled)
                    return ExitCancelled;

                var allRepoRoot = ReadDevBuildMarker();
                var allOutputDir = explicitOutputDir is not null
                    ? Path.GetFullPath(explicitOutputDir)
                    : ResolveDefaultOutputDir(component.Name);

                using var allSp = new ServiceCollection()
                    .AddSerilog(verbose: false, quiet: true)
                    .BuildServiceProvider();
                var allLoggerFactory = allSp.GetRequiredService<ILoggerFactory>();

                return RunAllForComponent(component, allOutputDir, allRepoRoot, allLoggerFactory);
            }

            if (explicitOutput is not null && explicitOutputDir is not null)
            {
                Console.Error.WriteLine("render: cannot combine --output and --output-dir.");
                return ExitUserError;
            }

            // Resolve output path — must have at least one positional arg (the fixture) when no --output
            if (componentArgs.Count == 0 && explicitOutput is null)
            {
                Console.Error.WriteLine($"render: missing fixture argument for component '{component.Name}'.");
                return ExitUserError;
            }

            string outputPath;
            if (explicitOutput is not null)
            {
                // Explicit --output: use as-is (validate fixture arg was provided separately)
                if (componentArgs.Count == 0)
                {
                    Console.Error.WriteLine($"render: missing fixture argument for component '{component.Name}'.");
                    return ExitUserError;
                }
                outputPath = Path.GetFullPath(explicitOutput);
            }
            else if (explicitOutputDir is not null)
            {
                var fixtureStem = Path.GetFileNameWithoutExtension(componentArgs[0]);
                outputPath = Path.Combine(
                    Path.GetFullPath(explicitOutputDir),
                    fixtureStem + "." + component.DefaultOutputExtension);
            }
            else
            {
                var fixtureStem = Path.GetFileNameWithoutExtension(componentArgs[0]);
                var outputDir = ResolveDefaultOutputDir(component.Name);
                outputPath = Path.Combine(outputDir, fixtureStem + "." + component.DefaultOutputExtension);
            }

            // Build inline ServiceCollection + Serilog (quiet: true so per-render Info doesn't pollute stdout)
            using var sp = new ServiceCollection()
                .AddSerilog(verbose: false, quiet: true)
                .BuildServiceProvider();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            if (_cancelled)
                return ExitCancelled;

            var ctx = new RenderContext(
                Args: componentArgs.ToArray(),
                OutputPath: outputPath,
                LoggerFactory: loggerFactory,
                RepoRoot: ReadDevBuildMarker());

            var result = component.Render(ctx);

            if (result.Success)
            {
                Console.Out.WriteLine($"{component.Name}/{Path.GetFileName(outputPath)} {result.Width}x{result.Height}");
                return ExitSuccess;
            }

            Console.Error.WriteLine($"render: {result.Error ?? "unknown error"}");
            return ExitRenderError;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"render: {ex.Message}");
            Log.Error(ex, "render unhandled");
            return ExitRenderError;
        }
    }

    /// <summary>
    /// Iterates all *.json fixtures in the component's default fixture directory and renders each one.
    /// Returns <c>ExitSuccess</c> if all succeeded, <c>ExitRenderError</c> if any failed,
    /// or <c>ExitCancelled</c> if <see cref="_cancelled"/> was set between iterations.
    /// </summary>
    private static int RunAllForComponent(
        IRenderableSurface component,
        string outputDir,
        string? repoRoot,
        ILoggerFactory loggerFactory)
    {
        var fixtureDir = repoRoot is not null
            ? Path.Combine(repoRoot, component.DefaultFixtureDir!)
            : Path.Combine(Environment.CurrentDirectory, component.DefaultFixtureDir!);

        if (!Directory.Exists(fixtureDir))
        {
            Console.Error.WriteLine($"render: fixture directory not found: {fixtureDir}");
            return ExitUserError;
        }

        Directory.CreateDirectory(outputDir);

        var logger = loggerFactory.CreateLogger(nameof(RenderCommand));
        bool anyFailure = false;
        foreach (var fixturePath in Directory.EnumerateFiles(fixtureDir, "*.json")
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            if (_cancelled)
            {
                Console.Error.WriteLine("render: cancelled.");
                return ExitCancelled;
            }

            var stem = Path.GetFileNameWithoutExtension(fixturePath);
            var outputPath = Path.Combine(outputDir, stem + "." + component.DefaultOutputExtension);

            var ctx = new RenderContext(
                Args: [fixturePath],
                OutputPath: outputPath,
                LoggerFactory: loggerFactory,
                RepoRoot: repoRoot);

            try
            {
                var result = component.Render(ctx);
                if (result.Success)
                {
                    Console.Out.WriteLine($"{component.Name}/{Path.GetFileName(outputPath)} {result.Width}x{result.Height}");
                }
                else
                {
                    Console.Error.WriteLine($"render: {fixturePath}: {result.Error ?? "unknown error"}");
                    anyFailure = true;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "render: unhandled exception for fixture {FixturePath}", fixturePath);
                Console.Error.WriteLine($"render: {fixturePath}: {ex.Message}");
                anyFailure = true;
            }
        }

        return anyFailure ? ExitRenderError : ExitSuccess;
    }

    /// <summary>
    /// Resolves the default output directory for a component.
    /// Prefers <c>~/.imrdy/.dev-build</c> repo root; falls back to the current directory.
    /// </summary>
    private static string ResolveDefaultOutputDir(string component)
    {
        var repoRoot = ReadDevBuildMarker();
        if (repoRoot is not null && Directory.Exists(repoRoot))
            return Path.Combine(repoRoot, "scratch", "views", component);

        return Path.Combine(Environment.CurrentDirectory, "scratch", "views", component);
    }

    /// <summary>
    /// Reads the repo root path from the dev-build marker file.
    /// Returns null if the marker does not exist or contains only whitespace.
    /// </summary>
    private static string? ReadDevBuildMarker()
    {
        if (!File.Exists(ImrdyPaths.DevBuildMarker))
            return null;
        var content = File.ReadAllText(ImrdyPaths.DevBuildMarker).Trim();
        return content.Length == 0 ? null : content;
    }

    private static void PrintHelp()
    {
        Console.Out.WriteLine("Usage:");
        Console.Out.WriteLine("  imrdy render <component> [args] [--output <path>]");
        Console.Out.WriteLine("  imrdy render <component> --all [--output-dir <dir>]");
        Console.Out.WriteLine("  imrdy render --all [--output-dir <dir>]");
        Console.Out.WriteLine("  imrdy render --list");
        Console.Out.WriteLine("  imrdy render --help");
        Console.Out.WriteLine();
        Console.Out.WriteLine("See `imrdy render --list` for registered components.");
    }

    private static void PrintList()
    {
        var components = RenderRegistry.Components;

        int maxNameLen = components.Max(c => c.Name.Length);
        int nameColWidth = maxNameLen + 2;

        Console.Out.WriteLine("Registered render components:");
        Console.Out.WriteLine();
        Console.Out.WriteLine($"  {"NAME".PadRight(nameColWidth)}DESCRIPTION");

        foreach (var c in components)
            Console.Out.WriteLine($"  {c.Name.PadRight(nameColWidth)}{c.Description}");
    }
}
