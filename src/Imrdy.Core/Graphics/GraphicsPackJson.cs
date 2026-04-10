namespace Imrdy.Core.Graphics;

/// <summary>
/// Record matching graphics pack.json schema.
/// </summary>
public sealed record GraphicsPackJson
{
    public string Name { get; init; } = string.Empty;
    public string Format { get; init; } = "svg";
    public string Version { get; init; } = "0.0.0";
    public string License { get; init; } = string.Empty;
    public Dictionary<string, GraphicsPackStateJson> States { get; init; } = new();
}

/// <summary>
/// Configuration for a single state entry within a graphics pack.
/// </summary>
public sealed record GraphicsPackStateJson
{
    public string File { get; init; } = string.Empty;
}
