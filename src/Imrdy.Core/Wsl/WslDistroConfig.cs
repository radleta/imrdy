using System.Text.Json.Serialization;

namespace Imrdy.Core.Wsl;

public sealed record WslDistroConfig
{
    [JsonPropertyName("watch_all")]
    public bool WatchAll { get; init; } = true;

    [JsonPropertyName("distros")]
    public List<WslDistroEntry>? Distros { get; init; }
}
