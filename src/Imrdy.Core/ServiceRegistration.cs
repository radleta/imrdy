using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Imrdy.Core.Graphics;
using Imrdy.Core.Sound;
using Imrdy.Core.State;
using Imrdy.Core.Validation;
using Imrdy.Core.Workspace;

namespace Imrdy.Core;

/// <summary>
/// Extension methods for registering Core services into a DI container.
/// </summary>
public static class ServiceRegistration
{
    /// <summary>
    /// Registers all Core singletons needed by any execution path.
    /// </summary>
    public static IServiceCollection AddCoreServices(this IServiceCollection services, string? workspacesPath = null)
    {
        services.AddSingleton<StateFileReader>();
        services.AddSingleton<PackLoader>();
        services.AddSingleton<GraphicsPackLoader>();
        services.AddSingleton<CooldownTracker>();
        services.AddSingleton<WorkspaceVisibility>();
        services.AddSingleton<PackValidator>();
        services.AddSingleton<ConfigValidator>();
        services.AddSingleton<WorkspaceValidator>();

        // WorkspaceStore needs a file path — default to ~/.imrdy/workspaces.json
        var wsPath = workspacesPath ?? ImrdyPaths.Workspaces;
        services.AddSingleton(new WorkspaceStore(wsPath));

        return services;
    }

    /// <summary>
    /// Configures Serilog with stderr console sink and optional file sink.
    /// Respects IMRDY_LOG=1 env var to switch to Debug level.
    /// Also defaults to Debug when the dev-build marker file exists at <see cref="ImrdyPaths.DevBuildMarker"/>
    /// (written by build-dev.sh) so local dev builds get diagnostic logging without env-var fiddling.
    /// </summary>
    public static IServiceCollection AddSerilog(
        this IServiceCollection services,
        bool verbose = false,
        bool quiet = false,
        bool fileSink = false,
        string? logPath = null)
    {
        var levelSwitch = new LoggingLevelSwitch();

        // Default: Information. Verbose: Debug. Quiet: Warning.
        if (verbose)
        {
            levelSwitch.MinimumLevel = LogEventLevel.Debug;
        }
        else if (quiet)
        {
            levelSwitch.MinimumLevel = LogEventLevel.Warning;
        }
        else
        {
            levelSwitch.MinimumLevel = LogEventLevel.Information;
        }

        // IMRDY_LOG=1 OR dev-build marker overrides to Debug (only if no explicit verbose/quiet).
        // The marker is written by build-dev.sh so local dev deploys get Debug logging by default
        // without requiring IMRDY_LOG=1 to be set in every shell that triggers a hook.
        if (!verbose && !quiet
            && (Environment.GetEnvironmentVariable("IMRDY_LOG") == "1"
                || File.Exists(ImrdyPaths.DevBuildMarker)))
        {
            levelSwitch.MinimumLevel = LogEventLevel.Debug;
        }

        var config = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(levelSwitch)
            .WriteTo.Console(
                standardErrorFromLevel: LogEventLevel.Verbose,
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");

        if (fileSink)
        {
            var logPath2 = logPath ?? ImrdyPaths.MonitorLog;
            config.WriteTo.File(
                logPath2,
                fileSizeLimitBytes: 1_048_576, // 1MB
                retainedFileCountLimit: 5,
                rollOnFileSizeLimit: true,
                shared: true,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}");
        }

        var logger = config.CreateLogger();
        Log.Logger = logger;

        services.AddSingleton(levelSwitch);
        services.AddLogging(builder => builder.AddSerilog(logger, dispose: true));

        return services;
    }
}
