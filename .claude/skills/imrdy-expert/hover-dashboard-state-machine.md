---
tags: [imrdy-expert/dashboard]
summary: "HoverDashboardControllerBase owns the dwell/grace state machine; derived controllers plug in domain-specific dispatch (TryHitTestForOurDomain → BuildViewModel → CreateForm → ShowForm → ApplyViewModelUpdate); cross-controller hide protocol via FormShown event wired in TrayApp"
last-verified: "2026-09-25"
---

# Hover Dashboard State Machine

## Base Controller Dispatch Chain

`HoverDashboardControllerBase` owns the dwell/grace/dismissal state machine. Derived controllers plug in domain-specific behavior via five abstract methods:

| Method | Role |
|---|---|
| `TryHitTestForOurDomain(clientX, out item, out hitIndex)` | Hit-test the overlay; return true only for items of this controller's domain type (`DisplayItemType.Session` or `DisplayItemType.Workspace`). Derived calls `_overlayWindow.TryHitTestAtClient` and filters by `item.ItemType`. |
| `BuildViewModel(item)` | Build the domain VM from the resolved `DisplayItem`. Returns `null` to suppress show (P7 suppression path). |
| `CreateForm(viewModel)` | Instantiate the domain form from the VM. |
| `ShowForm(form, viewModel)` | Call the typed `form.Show(TViewModel)` overload. |
| `ApplyViewModelUpdate(form, viewModel)` | Call the typed `form.Update(TViewModel)` overload. Used by switch detection and the throttled refresh. |

Extension points called by the base state machine:
- `OnSameItemRefreshTick(currentItem)` — called every `RefreshIntervalTicks=10` (~1s) while the form is visible on the same item. `SessionHoverDashboardController` overrides to call `RebuildAndApplyUpdate`; `WorkspaceHoverDashboardController` overrides to rebuild the VM with fresh `DateTimeOffset.UtcNow` so `ActivityText` advances.
- `OnFormShown(item, viewModel, cursor)` — called after the form is shown and pinned. `SessionHoverDashboardController` uses it to kick off an async git fetch.
- `OnFormHidden()` — called when the form hides. `SessionHoverDashboardController` uses it to null `_hoveredSessionId`.

## The Drain Tick

`TrayApp` calls `OnDrainTick()` on every 100ms drain-timer tick (UI thread). The controller reads `Cursor.Position` itself.

### The z-order gate

```
cursorOverOverlay = overlayBounds.Contains(cursor) && WindowAtPoint(cursor) == overlayHwnd
cursorOverForm    = formHwnd != 0 && WindowAtPoint(cursor) == formHwnd
```

`PInvokeOverlay.WindowAtPoint` (`WindowFromPoint`) is called once per tick. Geometric containment alone is not enough: an open tray or overlay context menu physically covers the overlay row while the overlay's `Bounds` is unchanged, so a purely geometric test lets cursor movement over the menu dwell a ghost dashboard into view. With the gate, any foreign topmost window at the cursor — menus, system popups, Win+Tab, the taskbar — fails the test and the dwell never accumulates.

Keep hover-intent detection on this gate. Do not reintroduce `_openTrayMenuCount` or `overlay.Visible = false` around menu lifetimes: that was a paper-over for purely geometric detection, it is redundant under the gate, and it was deliberately deleted. The accepted trade-off is that the overlay stays visible under an open menu.

### Path A — form hidden

1. **Post-interaction cooldown.** While `_awaitingOverlayExit` is set, the tick only clears it once `cursorOverOverlay` is false, keeps `_dwellTicks = 0`, and returns.
2. **Dwell.** While `cursorOverOverlay`, `_dwellTicks` increments; at `DwellThresholdTicks = 2` (~200ms) `TryShowForm` runs. Leaving the overlay resets it.

### Path B — form visible

- **Cursor over overlay or form** — `_outsideTicks` resets. If the cursor is over the overlay, **switch detection** runs (below).
- **Cursor in the bridge** — inside the union of overlay and form bounds inflated by `BridgeGap` (12px), and `_outsideTicks < BridgeTraversalGraceTicks` (2): hold steady.
- **Otherwise** — `_outsideTicks` increments; at `DismissThresholdTicks = 3` (~300ms) the form unpins and `HideForm()` starts the fade-out.
- **Throttled refresh** — every `RefreshIntervalTicks` while visible and not dismissing, `OnSameItemRefreshTick(_hoveredItem)`.

### Switch detection

A dashboard that stays open while the cursor crosses from item A to item B must follow the cursor, or it keeps showing A. In Path B, whenever the cursor is over the overlay, the base converts screen→client, calls `TryHitTestForOurDomain`, and when the hit item's `Id` differs from `_hoveredItem.Id` it sets `_hoveredItem`, rebuilds the VM and calls `ApplyViewModelUpdate` on the existing form — no recreate, no re-pin, no opacity reset. A cursor in the gap between chips misses the hit test and changes nothing.

This makes `Update(vm)` load-bearing: it must refresh **every** dynamic field, or the dashboard shows B's activity line under A's name. The field-promote pattern is the guard — see [WinForms Update Field-Promote](winforms-update-field-promote.md).

`SessionHoverDashboardController` fetches git info off the UI thread when it is not cached. Its continuation discards a result with `if (_hoveredSessionId != sessionId) return;`, and `_hoveredSessionId` is set before the fetch starts, so a result arriving after the user has moved on is dropped.

## Cross-Controller Hide Protocol

Two controllers run simultaneously (session + workspace). Only one dashboard should be visible at a time. The protocol:

1. `HoverDashboardControllerBase.FormShown` event — raised at the end of `TryShowForm`, after `OnFormShown` returns.
2. `HideIfVisible()` — idempotent method on each controller; triggers the existing fade-out if a form is currently shown. No-op when already hidden or already dismissing (`_opacityDirection == -1`).
3. TrayApp wires the cross-subscribe (P6 — wiring NOT in base ctor):
   ```csharp
   _hoverController.FormShown          += _workspaceHoverController.HideIfVisible;
   _workspaceHoverController.FormShown += _hoverController.HideIfVisible;
   ```

**Why wiring belongs in TrayApp:** the base ctor must not subscribe to peers because it doesn't know who its peer is. Subscribing from a derived ctor creates a coupling between peers that is invisible at the call site. TrayApp is the single place that knows both controllers exist — it is the canonical subscription site, and it re-wires both controllers whenever `OnConfigChanged` recreates the overlay.

**Anti-pattern**: controllers discovering peers via a shared registry and self-wiring on construction. This makes the hide protocol implicit and breaks when controllers are replaced.

## SurfaceInteracted — a click is a commitment

The grace corridor exists to tolerate cursor traversal. A click is different: the user clicked chip A and then chip B expects B's state, not A's dashboard lingering until the corridor expires. So `OverlayPanel` raises `SurfaceInteracted` after a successful **left-click** dispatch (inside the try, after the router call, so a router exception does not dismiss), and `HandleSurfaceInteraction()` runs:

```csharp
ForceHideForm();              // dispose immediately — no fade
_awaitingOverlayExit = true;  // post-interaction cooldown
```

`TrayApp.HandleOverlayDragCompleted` calls the same `HandleSurfaceInteraction()` on both controllers when a grip drag completes (see [Overlay Interactivity](overlay-interactivity.md)).

**Subscription lifecycle (P6):** TrayApp subscribes both controllers right after the overlay is constructed, unsubscribes them from the **old** overlay before disposing controllers and overlay in `OnConfigChanged`'s structural path and at shutdown, and resubscribes to the new one.

### Right-Click Does NOT Fire It (and Why That's a Constraint, Not a Gap)

Right-click, in either the chip-hit or the gutter sub-branch of `OverlayPanel.OnMouseUp`, never raises `SurfaceInteracted`. Making it do so — to tear a visible dashboard down before the menu opens — was tried and reverted after a live regression, and it will regress the same way in any position within the handler:

1. `OnMouseUp` fires; `SurfaceInteracted` → `ForceHideForm()` → `DisposeForm()` destroys the dashboard window synchronously.
2. The router opens the `ContextMenuStrip`.
3. `OnMouseUp` returns; the pump now delivers the **posted** activation/z-order fallout of step 1's window destruction.
4. `ToolStripManager.ModalMenuFilter` reads that fallout as an activation change and force-closes the menu that just opened.

The menu opens and dies in the same frame. The fingerprint is two-click: the first right-click makes the dashboard vanish with no menu; the second (nothing left to destroy) opens the menu normally. Ordering inside the synchronous handler — before, after, or in a `finally` — cannot move a *posted* message; a real fix would have to move the destroy relative to the message pump (e.g. `BeginInvoke`), and none is in place.

The related hazard that *is* fixed lives on the form: `HoverDashboardFormBase` overrides `ShowWithoutActivation => true`, so a dashboard is never the active window and the ordinary fade-dismiss that can fire while a menu is open does not change the active window. Keep that override.

Both right-click sub-branches keep their own try/catch around the router call, independent of this event.

## Post-Interaction Cooldown

After a left-click activates a session, the cursor usually still sits on the overlay row. Without a guard the next ticks dwell again and the dashboard for the item just clicked reappears. `_awaitingOverlayExit` suppresses Path A entirely until the cursor physically leaves the overlay; then dwell resumes normally.

| | Grace corridor | Post-interaction cooldown |
|---|---|---|
| **Active when** | Form is visible | Form is hidden, after a click or drag |
| **Purpose** | Tolerate cursor travel between overlay and form | Prevent dwell re-trigger after the user committed |
| **Exit condition** | Cursor outside the inflated union for `DismissThresholdTicks` | Cursor leaves the overlay |

## Anti-Patterns

| Anti-Pattern | Why Wrong | Fix |
|---|---|---|
| Dismissing only on corridor expiry | Doesn't handle an immediate click on a different chip | `SurfaceInteracted` for the commit path |
| Subscribing inside the controller | Couples controller to overlay implementation and to its peer | Subscribe in TrayApp |
| Purely geometric hover detection | Menus and popups over the overlay trigger ghost dwells | Gate on `WindowAtPoint == overlayHwnd` |
| Making right-click fire `SurfaceInteracted` | The destroyed dashboard's posted fallout closes the just-opened menu | Leave right-click out; see above |
| Updating only the fields that "changed" in `Update(vm)` | Switch detection shows stale fields from the previous item | `Update` reassigns every dynamic field |

## Related

- [Hover Dashboard Form Lifecycle](hover-dashboard-form-lifecycle.md) — placement, recreate-per-show and all-desktops pinning
- [Overlay Interactivity](overlay-interactivity.md) — `DragCompleted` and the overlay's event contract
- [Overlay Context Menus](overlay-context-menus.md) — how the overlay's right-click menu gets foreground
