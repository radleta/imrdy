using System.Text.Json.Serialization;

namespace Imrdy.Core.Sound;

/// <summary>
/// Record for ~/.claude/sounds/config.json.
/// </summary>
public sealed record SoundConfig
{
    [JsonPropertyName("default")]
    public string? Default { get; init; }

    [JsonPropertyName("projectMappings")]
    public Dictionary<string, string> ProjectMappings { get; init; } = new();

    [JsonPropertyName("soundEnabled")]
    public bool SoundEnabled { get; init; } = true;
}
