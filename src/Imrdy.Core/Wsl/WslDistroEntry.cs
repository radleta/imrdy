using System.Text.Json.Serialization;

namespace Imrdy.Core.Wsl;

public sealed record WslDistroEntry
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("linux_homes")]
    public List<string>? LinuxHomes { get; init; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    [JsonPropertyName("discovered_at")]
    public DateTimeOffset DiscoveredAt { get; init; }
}
