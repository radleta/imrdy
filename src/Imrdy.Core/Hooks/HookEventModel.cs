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

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; } = null;
}
