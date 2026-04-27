using Imrdy.Core.Diagnostics;
using Imrdy.Windows.Diagnostics;

namespace Imrdy.Windows.Commands;

/// <summary>
/// CLI client for the <c>render-live</c> IPC verb. Sends the request to the running tray
/// and saves the resulting PNG to the specified output path.
/// Stdout carries only the success summary; stderr carries errors.
/// Exit codes: 0 success / 1 user-input error / 2 infrastructure error.
/// </summary>
internal static class RenderLiveCommand
{
    public static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("render-live: missing session-id argument");
            Console.Error.WriteLine("Usage: imrdy render-live <session-id> --output <path>");
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
                    Console.Error.WriteLine("render-live: --output requires a path argument");
                    Console.Error.WriteLine("Usage: imrdy render-live <session-id> --output <path>");
                    return 1;
                }
                outputPath = Path.GetFullPath(args[i + 1]);
                break;
            }
        }

        if (outputPath is null)
        {
            Console.Error.WriteLine("render-live: --output <path> is required");
            Console.Error.WriteLine("Usage: imrdy render-live <session-id> --output <path>");
            return 1;
        }

        var request = new InspectRequest("render-live", sessionId, outputPath);

        InspectResponse response;
        try
        {
            response = InspectIpcClient.Send(request, TimeSpan.FromSeconds(5));
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine($"Tray not running. {ex.Message}");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"render-live: transport error — {ex.Message}");
            return 2;
        }

        if (response.Error is not null)
        {
            Console.Error.WriteLine($"render-live: {response.Error}");
            return 1;
        }

        if (response.Render is null)
        {
            Console.Error.WriteLine("render-live: server returned no render result");
            return 1;
        }

        Console.Out.WriteLine($"render-live: {Path.GetFileName(response.Render.OutputPath)} {response.Render.Width}x{response.Render.Height}");
        return 0;
    }
}
