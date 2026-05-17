namespace Imrdy.Windows.Dashboard;

/// <summary>
/// Controls which edge of the form is fixed when the form is resized.
/// Used by <see cref="HoverDashboardFormBase.PlaceWithAnchor"/> to keep the form abutting a
/// reference surface (the overlay row) even when the form's AutoSize height changes
/// after Show() or after a live-session-switch update changes optional row visibility.
/// </summary>
internal enum DashboardAnchor
{
    /// <summary>Form's TOP edge is fixed at AnchorY; growth is downward.</summary>
    Top,
    /// <summary>Form's BOTTOM edge is fixed at AnchorY; growth is upward (Top = AnchorY - Height).</summary>
    Bottom,
}
