namespace Imrdy.Core.Menus;

public sealed record WorkspaceMenuState
{
    public required string WorkspaceName { get; init; }
    public required string WorkspacePath { get; init; }
}
