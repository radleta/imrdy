using System.Text.Json.Serialization;

namespace Imrdy.Core.Workspace;

/// <summary>
/// Root JSON shape for ~/.imrdy/workspaces.json.
/// </summary>
public sealed record WorkspaceConfig
{
    [JsonPropertyName("workspaces")]
    public List<WorkspaceEntry> Workspaces { get; init; } = [];
}
