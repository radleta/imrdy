using Imrdy.Core.Time;
using Imrdy.Core.Workspace;

namespace Imrdy.Core.Display;

/// <summary>
/// Builds a <see cref="WorkspaceDashboardViewModel"/> from Core-only inputs.
/// Pure stateless function — no I/O, no WinForms dependency.
/// </summary>
public static class WorkspaceDashboardViewModelBuilder
{
    // requires: entry is non-null; entry.Path is non-null (required string per WorkspaceEntry);
    //           entry.Name is non-null (required string per WorkspaceEntry);
    //           cachedGit MAY be null (no git repo / cache miss);
    //           currentDesktopIndex MAY be null (no desktop info available);
    //           lastSeenAt MAY be null (workspace never observed in a session);
    //           now — represents "the moment of querying"; must be a UTC-offset value;
    //                  passed explicitly so Build is a pure function (D7).
    //
    // ensures:  result.WorkspacePath == entry.Path
    //           result.Name == entry.Name        // MUST use entry.Name directly (not ??)
    //           result.Desktop == entry.Desktop  // field on WorkspaceEntry is "Desktop"
    //           result.IconStyle == entry.IconStyle
    //           result.ActivityText == "never seen"                              (when lastSeenAt is null)
    //                              == "active " + FormatDuration(now - lastSeenAt.Value) + " ago"  (otherwise)
    //           result.Git == cachedGit
    //           result.IsCurrentDesktop ==
    //               (currentDesktopIndex.HasValue && currentDesktopIndex.Value == entry.Desktop)
    //
    // throws:   ArgumentNullException if entry is null
    public static WorkspaceDashboardViewModel Build(
        WorkspaceEntry entry,
        GitInfo? cachedGit,
        int? currentDesktopIndex,
        DateTimeOffset? lastSeenAt,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var activityText = lastSeenAt is null
            ? "never seen"
            : $"active {RelativeTimeFormatter.FormatDuration(now - lastSeenAt.Value)} ago";

        return new WorkspaceDashboardViewModel(
            WorkspacePath: entry.Path,
            Name: entry.Name,
            Desktop: entry.Desktop,
            IsCurrentDesktop: currentDesktopIndex.HasValue && currentDesktopIndex.Value == entry.Desktop,
            IconStyle: entry.IconStyle,
            ActivityText: activityText,
            Git: cachedGit);
    }
}
