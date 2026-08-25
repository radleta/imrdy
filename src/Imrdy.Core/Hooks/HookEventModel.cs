using System.Text.Json;
using System.Text.Json.Serialization;

namespace Imrdy.Core.Hooks;

/// <summary>
/// Typed model for Claude Code hook stdin JSON payload.
/// Covers all fields from the 20 hook event types.
/// </summary>
public sealed record HookEventModel
{
    [JsonPropertyName("hook_event_name")]
    public string HookEventName { get; init; } = "";

    [JsonPropertyName("session_id")]
    public string SessionId { get; init; } = "";

    [JsonPropertyName("session_name")]
    public string? SessionName { get; init; }

    [JsonPropertyName("cwd")]
    public string Cwd { get; init; } = "";

    [JsonPropertyName("source")]
    public string? Source { get; init; }

    [JsonPropertyName("notification_type")]
    public string? NotificationType { get; init; }

    [JsonPropertyName("prompt")]
    public string? Prompt { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("tool_name")]
    public string? ToolName { get; init; }

    [JsonPropertyName("agent_id")]
    public string? AgentId { get; init; }

    [JsonPropertyName("agent_type")]
    public string? AgentType { get; init; }

    [JsonPropertyName("wsl_distro")]
    public string? WslDistro { get; init; }

    /// <summary>
    /// The running-work roster, carried only on <c>Stop</c> and <c>SubagentStop</c> payloads
    /// (13/13 <c>Stop</c> and 96/96 <c>SubagentStop</c> across the whole of
    /// <c>evidence/capture.log</c> — every occurrence of either event carries the key). <c>null</c>
    /// means the field is absent from the payload (no information); an empty list means the field
    /// was present and measured empty (nothing is running). The two are not interchangeable — do
    /// not collapse <c>[]</c> to <c>null</c> or vice versa anywhere on this path.
    /// </summary>
    [JsonPropertyName("background_tasks")]
    public List<BackgroundTaskModel>? BackgroundTasks { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; } = null;
}
