using System.Reflection;
using Imrdy.Core.Hooks;

namespace Imrdy.Linux;

internal static class Program
{
    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "hook")
        {
            try
            {
                using var services = HookServiceBuilder.Build();
                _ = HookCommand.Run(services, Console.In, new LinuxHookEnvironment());
            }
            catch (Exception ex)
            {
                // Never fail the Claude session — hook errors are logged inside HookCommand.Run.
                // Exceptions here are unexpected (e.g., DI build failure before the logger is
                // available); write to stderr so the operator can diagnose from hook process output.
                Console.Error.WriteLine($"imrdy hook: unexpected error: {ex}");
            }

            return 0;
        }

        if (args.Length > 0 && (args[0] == "--version" || args[0] == "-v"))
        {
            var version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                ?? "dev";
            Console.WriteLine($"imrdy {version}");
            return 0;
        }

        if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
        {
            Console.WriteLine("imrdy - Claude Code session monitor hook");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  imrdy hook          Process a Claude Code hook event from stdin");
            Console.WriteLine("  imrdy --version     Show version");
            Console.WriteLine("  imrdy --help        Show this help");
            return 0;
        }

        Console.Error.WriteLine($"imrdy: unrecognized command '{args[0]}'");
        Console.Error.WriteLine("Run 'imrdy --help' for usage.");
        return 1;
    }
}
