namespace Imrdy.Windows.Menus;

/// <summary>
/// Pure decision logic behind the Step 08 first-right-click-eaten fix. Every menu builder in
/// this project (<see cref="SessionMenuBuilder"/>, <see cref="OverlayMenuBuilder"/>,
/// <see cref="WorkspaceMenuBuilder"/>, <see cref="ControllerMenuBuilder"/>) constructs its
/// <see cref="System.Windows.Forms.ContextMenuStrip"/> with zero items and rebuilds the item
/// collection entirely inside the <c>Opening</c> handler via <c>MenuRenderer.Apply</c>.
/// <see cref="System.Windows.Forms.ContextMenuStrip"/>'s own <c>OnOpening</c> override sets
/// <c>e.Cancel = true</c> whenever <c>Items.Count == 0</c> — and it does so BEFORE raising
/// <c>Opening</c> to subscribers, using the item count as it stood at that moment (i.e.
/// always zero, on the very first show of a fresh instance). A handler that populates items
/// but never clears that pre-set flag leaves the menu refused: WinForms will not display a
/// <see cref="System.Windows.Forms.ContextMenuStrip"/> whose <c>Opening</c> handler leaves
/// <c>e.Cancel == true</c>, regardless of how many items were added afterward. Every
/// subsequent open of that same instance starts from a non-zero item count (left over from
/// the previous successful rebuild), so WinForms never auto-cancels again — which is why the
/// defect presented as "first right-click on a never-before-opened session is eaten, but it
/// works every time after."
/// </summary>
internal static class MenuOpeningPolicy
{
    /// <summary>
    /// True when the Opening handler's rebuild produced at least one item and should
    /// therefore clear WinForms' pre-set <c>e.Cancel = true</c> so the menu actually
    /// displays. False when the rebuild legitimately produced zero items — in which case
    /// WinForms' original refusal is correct, since showing an empty menu is worse than
    /// showing none.
    /// </summary>
    /// <remarks>
    /// Callers must only invoke this — and only act on a <c>true</c> result by clearing
    /// <c>e.Cancel</c> — after a rebuild that completed without throwing. If the rebuild
    /// throws partway through, the item collection may be partial or reflect a
    /// never-cleared collection from a state-provider failure that occurred before
    /// <c>menu.Items.Clear()</c> ran; the caller's <c>catch</c> block must skip the
    /// cancel-clearing step entirely in that case (natural fall-through of a thrown
    /// exception past a subsequent statement already achieves this — see each builder's
    /// <c>Opening</c> handler).
    /// </remarks>
    public static bool ShouldClearCancel(int itemCount) => itemCount > 0;
}
