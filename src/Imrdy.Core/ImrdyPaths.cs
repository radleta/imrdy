namespace Imrdy.Core;

/// <summary>
/// Centralized path constants for ~/.imrdy/ directory structure.
/// All config, sessions, sounds, and logs live under this root.
/// Supports IMRDY_HOME override for testing.
/// </summary>
public static class ImrdyPaths
{
    public static string Home { get; }
    public static string Config { get; }
    public static string Sessions { get; }
    public static string Workspaces { get; }
    public static string SoundsDir { get; }
    public static string PacksDir { get; }
    public static string LogsDir { get; }
    public static string MonitorLog { get; }

    public const string MutexName = @"Global\ImrdyMonitor";

    static ImrdyPaths()
    {
        Home = Environment.GetEnvironmentVariable("IMRDY_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".imrdy");

        Config = Path.Combine(Home, "config.json");
        Sessions = Path.Combine(Home, "sessions");
        Workspaces = Path.Combine(Home, "workspaces.json");
        SoundsDir = Path.Combine(Home, "sounds");
        PacksDir = Path.Combine(Home, "sounds", "packs");
        LogsDir = Path.Combine(Home, "logs");
        MonitorLog = Path.Combine(Home, "logs", "monitor.log");
    }
}
