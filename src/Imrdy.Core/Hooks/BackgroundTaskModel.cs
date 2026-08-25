using System.Text.Json.Serialization;

namespace Imrdy.Core.Hooks;

/// <summary>
/// A single entry from the `background_tasks` roster carried on `Stop` and `SubagentStop` hook
/// payloads. Two observed <see cref="Type"/> values: `"subagent"` (adds <see cref="AgentType"/>,
/// no `command`) and `"shell"` (adds `command`, no <see cref="AgentType"/>). `command` is
/// intentionally unmodelled per spec §4.1 — it can be an arbitrarily long shell string and
/// <see cref="Description"/> already summarises it.
/// </summary>
public sealed record BackgroundTaskModel
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("type")]
    public string Type { get; init; } = "";

    [JsonPropertyName("status")]
    public string Status { get; init; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("agent_type")]
    public string? AgentType { get; init; }
}
