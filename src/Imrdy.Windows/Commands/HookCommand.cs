using System.Text.Json;
using Imrdy.Core;
using Imrdy.Core.Desktop;
using Imrdy.Core.Hooks;
using Imrdy.Core.State;
using Imrdy.Core.Status;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Imrdy.Windows.Commands;

/// <summary>
/// Implements the `imrdy hook` fast-path subcommand.
/// Reads Claude Code hook JSON from stdin, derives status, and writes an atomic state file.
/// </summary>
internal static class HookCommand
{
    /// <summary>
    /// Runs the hook command. Reads JSON from the provided TextReader (stdin),
    /// processes the hook event, and writes the state file.
    /// Returns exit code 0 on success, 1 on error.
    /// </summary>
    public static int Run(ServiceProvider services, TextReader stdin)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("HookCommand");

        string? input;
        try
        {
            input = stdin.ReadToEnd();
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "Failed to read stdin");
            return 1;
        }

        // Empty stdin is valid — exit cleanly (e.g., piped empty input)
        if (string.IsNullOrWhiteSpace(input))
        {
            logger.LogDebug("Empty stdin, exiting cleanly");
            return 0;
        }

        HookEventModel? hookEvent;
        try
        {
            hookEvent = JsonSerializer.Deserialize(input, ImrdyJsonContext.Default.HookEventModel);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse hook JSON");
            return 1;
        }

        if (hookEvent is null || string.IsNullOrEmpty(hookEvent.SessionId))
        {
            logger.LogError("Hook event is null or missing session_id");
            return 1;
        }

        // Validate session_id is a safe filename (Claude Code uses UUIDs)
        if (!IsValidSessionId(hookEvent.SessionId))
        {
            logger.LogError("Invalid session_id format: contains unsafe characters");
            return 1;
        }

        logger.LogDebug("Hook event: {Event} for session {SessionId}",
            hookEvent.HookEventName, hookEvent.SessionId);

        // Derive status from hook event
        var status = StatusDerivation.DeriveStatus(
            hookEvent.HookEventName,
            hookEvent.Source,
            hookEvent.NotificationType);

        logger.LogInformation("Hook: {SessionId} → {Status} ({HookEvent})",
            hookEvent.SessionId, status, hookEvent.HookEventName);
        logger.LogDebug("Hook raw stdin: {RawStdin}", input);

        // Normalize path and derive project name
        var normalizedCwd = PathNormalizer.Normalize(hookEvent.Cwd);
        var project = PathNormalizer.DeriveProject(hookEvent.Cwd);

        // Resolve Claude PID (best-effort — don't fail the hook if this fails)
        int? claudePid = null;
        try
        {
            var currentPid = Environment.ProcessId;
            claudePid = ProcessResolver.ResolveTerminalPid(currentPid, hookEvent.SessionId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to resolve Claude PID");
        }

        // Read existing state file for field preservation
        var reader = services.GetRequiredService<StateFileReader>();
        var statePath = Path.Combine(ImrdyPaths.Sessions, $"{hookEvent.SessionId}.json");
        var existing = reader.ReadStateFile(statePath);

        // Resolve last message
        var lastMessage = FieldPreservation.ResolveLastMessage(
            hookEvent.Prompt,
            hookEvent.Message,
            existing?.LastMessage);

        // Build new state
        var newState = new StateFileModel
        {
            SessionId = hookEvent.SessionId,
            Status = status,
            Project = project,
            Cwd = normalizedCwd,
            HookEvent = hookEvent.HookEventName,
            NotificationType = hookEvent.NotificationType ?? "",
            LastMessage = lastMessage,
            ClaudePid = claudePid,
            Timestamp = DateTimeOffset.UtcNow,
            SessionName = hookEvent.SessionName,
            ToolName = hookEvent.ToolName,
        };

        // Preserve sound_pack and desktop_index from existing state
        newState = FieldPreservation.PreserveFields(newState, existing);

        // Write atomic state file (StateFileReader.WriteStateFile creates the directory if needed)
        try
        {
            reader.WriteStateFile(statePath, newState);
            logger.LogDebug("State file written: {Path}", statePath);
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "Failed to write state file: {Path}", statePath);
            return 1;
        }

        // Auto-spawn tray if not running (mutex-gated, config-disablable)
        try
        {
            var config = ConfigReader.Read();
            if (config.Tray.Enabled)
                TraySpawner.EnsureRunning(logger);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Tray auto-spawn failed");
        }

        // Clean up session end
        if (string.Equals(hookEvent.HookEventName, "SessionEnd", StringComparison.OrdinalIgnoreCase))
        {
            ProcessResolver.ClearSession(hookEvent.SessionId);
        }

        return 0;
    }

    /// <summary>
    /// Validates that a session ID contains only safe filename characters.
    /// Claude Code uses UUID-format session IDs (alphanumeric + hyphens).
    /// Rejects path separators, dots-dots, and other unsafe characters to prevent path traversal.
    /// </summary>
    private static bool IsValidSessionId(string sessionId)
    {
        foreach (var c in sessionId)
        {
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
            {
                return false;
            }
        }

        return true;
    }
}
