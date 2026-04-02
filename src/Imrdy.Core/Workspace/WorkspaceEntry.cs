using System.Text.Json.Serialization;

namespace Imrdy.Core.Workspace;

/// <summary>
/// A pinned workspace entry: path, display name, and assigned desktop.
/// </summary>
public sealed record WorkspaceEntry
{
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("desktop")]
    public int Desktop { get; init; }
}
