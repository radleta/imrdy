namespace Imrdy.Core.Display;

/// <summary>
/// Serializable view model that drives the workspace hover dashboard.
/// <para>
/// This record is the complete render contract: every field is a ready-to-display value.
/// No form or renderer may perform clock arithmetic — all time-derived strings are
/// computed by <see cref="WorkspaceDashboardViewModelBuilder.Build"/> and stored here.
/// </para>
/// </summary>
public sealed record WorkspaceDashboardViewModel(
    string WorkspacePath,
    string Name,
    int Desktop,
    bool IsCurrentDesktop,
    string? IconStyle,
    /// <summary>
    /// Pre-computed activity string: "never seen" when the workspace has never been
    /// observed in a session, otherwise "active {duration} ago" relative to the
    /// <c>now</c> snapshot passed to <see cref="WorkspaceDashboardViewModelBuilder.Build"/>.
    /// </summary>
    string ActivityText,
    GitInfo? Git);
