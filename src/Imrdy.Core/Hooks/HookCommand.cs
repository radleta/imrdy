using System.Text.Json;
using Imrdy.Core.State;
using Imrdy.Core.Status;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Imrdy.Core.Hooks;

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
    public static int Run(ServiceProvider services, TextReader stdin, IHookEnvironment hookEnvironment)
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

        // Single-line hook log with all payload fields for grep-friendly diagnostics
        var parts = new List<string>(8)
        {
            $"{hookEvent.HookEventName}"
        };
        if (!string.IsNullOrEmpty(hookEvent.NotificationType))
            parts.Add($"type={hookEvent.NotificationType}");
        if (!string.IsNullOrEmpty(hookEvent.ToolName))
            parts.Add($"tool={hookEvent.ToolName}");
        if (!string.IsNullOrEmpty(hookEvent.Source))
            parts.Add($"source={hookEvent.Source}");
        if (!string.IsNullOrEmpty(hookEvent.Message))
            parts.Add($"msg={StateFileModel.TruncateMessage(hookEvent.Message, 80)}");
        if (hookEvent.ExtensionData is { Count: > 0 })
        {
            foreach (var kv in hookEvent.ExtensionData)
                parts.Add($"{kv.Key}={kv.Value}");
        }
        var detailStr = string.Join(" ", parts);
        if (!string.IsNullOrEmpty(hookEvent.AgentId))
            logger.LogInformation("Hook: {SessionId} → {Status} ({Details}) [teammate agent={AgentId}]",
                hookEvent.SessionId, status, detailStr, hookEvent.AgentId);
        else
            logger.LogInformation("Hook: {SessionId} → {Status} ({Details})",
                hookEvent.SessionId, status, detailStr);

        logger.LogDebug("Hook raw stdin: {RawStdin}", input);

        // Read existing state file for field preservation
        var reader = services.GetRequiredService<StateFileReader>();
        var statePath = Path.Combine(ImrdyPaths.Sessions, $"{hookEvent.SessionId}.json");
        var existing = reader.ReadStateFile(statePath);

        // Teammate events (agent_id present): update last_teammate_at timestamp.
        // Preserves lead status except when clearing a resolved "permission" state.
        if (!string.IsNullOrEmpty(hookEvent.AgentId))
        {
            if (existing is not null)
            {
                var updated = TeammateGate.ApplyTeammateEvent(existing, hookEvent.HookEventName);

                if (updated.Status != existing.Status)
                {
                    logger.LogInformation("Hook: {SessionId} → {Status} (teammate cleared permission, was {Event})",
                        hookEvent.SessionId, updated.Status, hookEvent.HookEventName);
                }

                try
                {
                    reader.WriteStateFile(statePath, updated);
                }
                catch (IOException ex)
                {
                    logger.LogError(ex, "Failed to write teammate timestamp: {Path}", statePath);
                    return 1;
                }
            }
            else
            {
                logger.LogWarning("Teammate hook fired before lead session exists: {SessionId} agent={AgentId}",
                    hookEvent.SessionId, hookEvent.AgentId);
            }

            // Auto-spawn tray if not running (same as lead path).
            // NOT gated on tray.enabled or overlay.enabled — the process does more than
            // render UI (state tracking, dwell timers, toasts, sounds, hot-reload of config).
            // Users can toggle display surfaces at runtime; the monitor must be alive to
            // respond. The proper escape hatch for headless/CI is the IMRDY_NO_TRAY env
            // var, which the platform EnsureTrayRunning implementation honors.
            try
            {
                hookEnvironment.EnsureTrayRunning();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Tray auto-spawn failed");
            }

            return 0;
        }

        // Lead events (no agent_id): full state file write
        var normalizedCwd = hookEnvironment.NormalizeCwd(hookEvent.Cwd);
        var project = Path.GetFileName(hookEnvironment.NormalizeCwd(hookEvent.Cwd) ?? "");

        // Resolve Claude PID (best-effort — don't fail the hook if this fails)
        int? claudePid = null;
        try
        {
            var currentPid = Environment.ProcessId;
            claudePid = hookEnvironment.ResolveTerminalPid(currentPid, hookEvent.SessionId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to resolve Claude PID");
        }

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
            WslDistro = hookEvent.WslDistro,
        };

        // Populate StartedAt on the first SessionStart — persisted via FieldPreservation on all
        // subsequent writes. Guard on existing?.StartedAt so a second SessionStart (reconnect or
        // tray restart) does not overwrite the original session start time.
        if (string.Equals(hookEvent.HookEventName, "SessionStart", StringComparison.OrdinalIgnoreCase)
            && existing?.StartedAt is null)
        {
            newState = newState with { StartedAt = DateTimeOffset.UtcNow };
        }

        // Preserve sound_pack, desktop_index, icon_style, started_at, and has_teammates from existing state
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

        // Auto-spawn tray if not running (mutex-gated, IMRDY_NO_TRAY-disablable).
        // NOT gated on config flags — see comment at the teammate-path spawn above.
        try
        {
            hookEnvironment.EnsureTrayRunning();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Tray auto-spawn failed");
        }

        // Clean up session end
        if (string.Equals(hookEvent.HookEventName, "SessionEnd", StringComparison.OrdinalIgnoreCase))
        {
            hookEnvironment.OnSessionEnd(hookEvent.SessionId);
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
