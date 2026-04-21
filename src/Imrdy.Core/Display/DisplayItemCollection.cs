namespace Imrdy.Core.Display;

/// <summary>Builds sorted display-item lists for the tray and overlay from pre-computed inputs.</summary>
public static class DisplayItemCollection
{
    /// <summary>
    /// Filters, maps, and sorts <paramref name="items"/> into tray and overlay lists.
    /// </summary>
    /// <param name="items">Caller-supplied inputs with pre-computed <see cref="DisplayItemInput.IsVisible"/> flags.</param>
    /// <param name="trayEnabled">When <c>false</c>, <see cref="BuiltDisplayItems.ForTray"/> is empty.</param>
    public static BuiltDisplayItems Build(
        IReadOnlyList<DisplayItemInput> items,
        bool trayEnabled)
    {
        // Filter to visible items, map to output record, then sort.
        // Sort: Session-before-Workspace via explicit map; do not depend on enum ordinal.
        var sorted = items
            .Where(x => x.IsVisible)
            .Select(x => new DisplayItem(
                x.Id,
                x.ItemType,
                x.Status,
                x.DesktopIndex,
                x.IconStyle,
                x.AgingTier,
                x.IsVisible,
                x.Label))
            .OrderBy(x => x.DesktopIndex.HasValue ? 0 : 1) // null DesktopIndex last
            .ThenBy(x => x.DesktopIndex ?? 0)
            .ThenBy(x => x.ItemType == DisplayItemType.Session ? 0 : 1) // Session-before-Workspace via explicit map; do not depend on enum ordinal.
            .ToList();

        IReadOnlyList<DisplayItem> forTray = trayEnabled
            ? sorted
            : Array.Empty<DisplayItem>();

        return new BuiltDisplayItems(forTray, sorted);
    }
}
