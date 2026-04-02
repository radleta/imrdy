using Imrdy.Core.Desktop;
using Imrdy.Core.State;

namespace Imrdy.Core.Workspace;

/// <summary>
/// Determines white dot visibility per workspace (D11, D22).
/// Visible when no active sessions match the workspace path.
/// Desktop auto-tracked from latest session; persisted on hidden→visible transition.
/// </summary>
public sealed class WorkspaceVisibility
{
    private readonly Dictionary<string, bool> _previouslyVisible = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _lastTrackedDesktop = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Result of evaluating visibility for a single workspace.
    /// </summary>
    public sealed record VisibilityResult
    {
        public required WorkspaceEntry Workspace { get; init; }
        public required bool IsVisible { get; init; }
        public required int TrackedDesktop { get; init; }
        public required bool DesktopChanged { get; init; }
    }

    /// <summary>
    /// Evaluates visibility and desktop tracking for all workspaces given active sessions.
    /// </summary>
    public IReadOnlyList<VisibilityResult> Evaluate(
        IReadOnlyList<WorkspaceEntry> workspaces,
        IReadOnlyList<StateFileModel> activeSessions)
    {
        var results = new List<VisibilityResult>(workspaces.Count);

        foreach (var workspace in workspaces)
        {
            var normalizedPath = PathNormalizer.Normalize(workspace.Path);
            var matchingSessions = new List<StateFileModel>();

            foreach (var session in activeSessions)
            {
                if (PathNormalizer.AreEqual(session.Cwd, normalizedPath))
                {
                    matchingSessions.Add(session);
                }
            }

            var hasActiveSessions = matchingSessions.Count > 0;
            var isVisible = !hasActiveSessions;

            // Desktop auto-tracking: use latest session's desktop (D16, D22)
            var trackedDesktop = workspace.Desktop;
            if (hasActiveSessions)
            {
                var latestSession = matchingSessions
                    .Where(s => s.DesktopIndex.HasValue)
                    .OrderByDescending(s => s.Timestamp)
                    .FirstOrDefault();

                if (latestSession?.DesktopIndex is { } sessionDesktop)
                {
                    trackedDesktop = sessionDesktop;
                    _lastTrackedDesktop[normalizedPath] = sessionDesktop;
                }
            }

            // Detect hidden→visible transition for desktop persistence (D22)
            var wasVisible = _previouslyVisible.GetValueOrDefault(normalizedPath, true);
            var desktopChanged = false;

            if (isVisible && !wasVisible
                && _lastTrackedDesktop.TryGetValue(normalizedPath, out var lastDesktop)
                && lastDesktop != workspace.Desktop)
            {
                desktopChanged = true;
                trackedDesktop = lastDesktop;
            }

            _previouslyVisible[normalizedPath] = isVisible;

            results.Add(new VisibilityResult
            {
                Workspace = workspace,
                IsVisible = isVisible,
                TrackedDesktop = trackedDesktop,
                DesktopChanged = desktopChanged,
            });
        }

        return results;
    }

    /// <summary>
    /// Clears all tracked visibility state.
    /// </summary>
    public void Clear()
    {
        _previouslyVisible.Clear();
        _lastTrackedDesktop.Clear();
    }
}
