namespace Imrdy.Windows.Models;

/// <summary>
/// Command-line options for the tray monitor. Matches PS1 reference parameters.
/// </summary>
public sealed record MonitorOptions
{
    /// <summary>Minutes before stale sessions are removed (default 60).</summary>
    public int StaleMinutes { get; init; } = 60;

    /// <summary>Disable all toast notifications.</summary>
    public bool NoToast { get; init; }

    /// <summary>Disable all sounds (overrides config).</summary>
    public bool Silent { get; init; }

    /// <summary>
    /// Parses monitor options from command-line args.
    /// Unrecognized args are silently ignored.
    /// </summary>
    public static MonitorOptions Parse(string[] args)
    {
        var options = new MonitorOptions();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--stale-minutes" when i + 1 < args.Length && int.TryParse(args[i + 1], out var minutes):
                    options = options with { StaleMinutes = minutes };
                    i++;
                    break;
                case "--no-toast":
                    options = options with { NoToast = true };
                    break;
                case "--silent":
                    options = options with { Silent = true };
                    break;
            }
        }

        return options;
    }
}
