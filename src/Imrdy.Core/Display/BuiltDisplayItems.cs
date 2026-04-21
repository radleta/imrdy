namespace Imrdy.Core.Display;

/// <summary>Result of <see cref="DisplayItemCollection.Build"/>: pre-sorted item lists for the tray and overlay.</summary>
public sealed record BuiltDisplayItems(
    IReadOnlyList<DisplayItem> ForTray,
    IReadOnlyList<DisplayItem> ForOverlay);
