using Imrdy.Windows.Interaction;

namespace Imrdy.Windows.Rendering;

/// <summary>
/// No-op <see cref="ISessionInteractionRouter"/> for the render path.
/// <see cref="OverlayPanel"/> requires a non-nullable router; during
/// <c>DrawToBitmap</c> no actual interaction occurs, so all methods are empty.
/// </summary>
internal sealed class NullSessionInteractionRouter : ISessionInteractionRouter
{
    public static readonly NullSessionInteractionRouter Instance = new();

    public void ActivateSession(string sessionId) { }
    public void ActivateWorkspace(string workspacePath) { }
    public void OpenSessionMenu(string sessionId, MenuAnchor anchor) { }
    public void OpenWorkspaceMenu(string workspacePath, MenuAnchor anchor) { }
    public void OpenOverlayMenu(MenuAnchor anchor) { }
}
