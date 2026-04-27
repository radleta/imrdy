using System.Text.Json;
using Imrdy.Core;
using Imrdy.Core.Diagnostics;
using Imrdy.Windows.Diagnostics;

namespace Imrdy.Windows.Commands;

/// <summary>
/// CLI client for the <c>inspect-live</c> IPC verb. Sends the request to the running tray
/// and prints the JSON response. Stdout carries only data; stderr carries diagnostics.
/// Exit codes: 0 success / 1 user-input error / 2 infrastructure error.
/// </summary>
internal static class InspectLiveCommand
{
    public static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("inspect-live: missing session-id argument");
            Console.Error.WriteLine("Usage: imrdy inspect-live <session-id> [--output <path>]");
            return 1;
        }

        var sessionId = args[0];
        string? outputPath = null;

        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] is "--output" or "-o")
            {
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine("inspect-live: --output requires a path argument");
                    Console.Error.WriteLine("Usage: imrdy inspect-live <session-id> [--output <path>]");
                    return 1;
                }
                outputPath = Path.GetFullPath(args[i + 1]);
                break;
            }
        }

        var request = new InspectRequest("inspect-live", sessionId, outputPath);

        InspectResponse response;
        try
        {
            response = InspectIpcClient.Send(request, TimeSpan.FromSeconds(2));
        }
        catch (InvalidOperationException ex)
        {
            // Tray not running or IPC not enabled
            Console.Error.WriteLine($"Tray not running. {ex.Message}");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"inspect-live: transport error — {ex.Message}");
            return 2;
        }

        if (response.Error is not null)
        {
            Console.Error.WriteLine($"inspect-live: {response.Error}");
            // Server-level errors (session not found, etc.) are user/input errors
            return 1;
        }

        if (outputPath is not null)
        {
            try
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(response, ImrdyJsonContext.Default.InspectResponse);
                File.WriteAllBytes(outputPath, bytes);
                Console.Out.WriteLine($"inspect-live: wrote {outputPath}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"inspect-live: failed to write output file — {ex.Message}");
                return 2;
            }
        }
        else
        {
            // Indented pretty-print to stdout for piping/agent consumption.
            var json = JsonSerializer.Serialize(response, ImrdyJsonContext.Indented);
            Console.Out.WriteLine(json);
        }

        return 0;
    }
}
