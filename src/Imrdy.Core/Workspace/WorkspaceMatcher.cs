using Imrdy.Core.Desktop;

namespace Imrdy.Core.Workspace;

/// <summary>
/// Matches a session's cwd to a pinned workspace using exact normalized path comparison (D20).
/// </summary>
public static class WorkspaceMatcher
{
    /// <summary>
    /// Finds the workspace whose path matches the session cwd exactly (normalized, case-insensitive).
    /// Returns null if no match.
    /// </summary>
    public static WorkspaceEntry? Match(IReadOnlyList<WorkspaceEntry> workspaces, string sessionCwd)
    {
        if (string.IsNullOrWhiteSpace(sessionCwd))
        {
            return null;
        }

        foreach (var workspace in workspaces)
        {
            if (PathNormalizer.AreEqual(workspace.Path, sessionCwd))
            {
                return workspace;
            }
        }

        return null;
    }
}
