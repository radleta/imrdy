namespace Imrdy.Core.Display;

/// <summary>Pure-data view of one tray/overlay item, produced by <see cref="DisplayItemCollection.Build"/>.</summary>
public sealed record DisplayItem(
    string Id,
    DisplayItemType ItemType,
    string Status,
    int? DesktopIndex,
    string IconStyle,
    int AgingTier,
    bool IsVisible,
    string Label);
