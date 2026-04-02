using System.Text.Json.Serialization;

namespace Imrdy.Core.Sound;

/// <summary>
/// Record matching pack.json schema.
/// </summary>
public sealed record PackJson
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("version")]
    public string Version { get; init; } = "";

    [JsonPropertyName("events")]
    public Dictionary<string, EventConfig> Events { get; init; } = new();
}

/// <summary>
/// Configuration for a single sound event within a pack.
/// </summary>
public sealed record EventConfig
{
    [JsonPropertyName("folder")]
    public string Folder { get; init; } = "";
}
