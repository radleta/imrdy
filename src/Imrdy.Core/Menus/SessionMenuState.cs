namespace Imrdy.Core.Menus;

public sealed record SessionMenuState
{
    public required string SessionId { get; init; }
    public required string Status { get; init; }
    public string? Project { get; init; }
}
