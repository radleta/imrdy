namespace Imrdy.Windows.Interaction;

/// <summary>
/// Single entry point for every user-initiated session/workspace interaction,
/// regardless of surface (tray NotifyIcon, overlay form, toast notification,
/// controller menu, CLI, future IPC, etc.).
///
/// Implementations (see <c>TrayApp</c>) guarantee a uniform pre-dispatch step —
/// <c>MarkSessionInteracted</c> / <c>MarkWorkspaceInteracted</c> — so every call
/// site gets age-reset and icon brighten for free. Call sites MUST NOT call
/// <c>SwitchToSessionDesktop</c>, <c>SwitchToWorkspaceDesktop</c>, <c>menu.Show</c>,
/// or <c>NotifyIconMenuHost.Show</c> directly.
///
/// <para>
/// Adding a new interaction surface (global hotkey, HTTP endpoint, etc.) means
/// implementing one call site against this interface — not re-deriving
/// interaction semantics. Adding a new interaction verb (e.g. DismissSession)
/// means one method on this interface with one implementation — all surfaces
/// get the new verb for free.
/// </para>
/// </summary>
internal interface ISessionInteractionRouter
{
    /// <summary>Primary action: switch focus / desktop to the session.</summary>
    void ActivateSession(string sessionId);

    /// <summary>Primary action: switch focus / desktop to the workspace.</summary>
    void ActivateWorkspace(string workspacePath);

    /// <summary>Secondary action: open the session's context menu at the given anchor.</summary>
    void OpenSessionMenu(string sessionId, MenuAnchor anchor);

    /// <summary>Secondary action: open the workspace's context menu at the given anchor.</summary>
    void OpenWorkspaceMenu(string workspacePath, MenuAnchor anchor);
}
