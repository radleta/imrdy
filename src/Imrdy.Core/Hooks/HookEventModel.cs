using System.Text.Json.Serialization;

namespace Imrdy.Core.Hooks;

/// <summary>
/// Typed model for Claude Code hook stdin JSON payload.
/// Covers all fields from the 8 hook event types.
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
}
