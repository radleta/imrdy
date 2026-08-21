---
tags: [imrdy-expert/overlay]
summary: "DragCompleted event fires at end of drag-to-reposition in OnMouseUp; companion to SurfaceInteracted with separate contract; subscription lifecycle identical (P6 TrayApp owns wiring)"
code-cites: []
---

## DragCompleted Event

`OverlayPanel` exposes `DragCompleted` (fires at the end of the drag-to-reposition branch in `OnMouseUp`, before `ResetDragState()`). This event complements `SurfaceInteracted` — `DragCompleted` fires only for a completed drag; right-click does NOT fire `DragCompleted`.

**Correction (a prior revision of this paragraph was itself wrong):** This page originally said "right-click does NOT fire either event," which is accurate. A later revision claimed a menu-dismissal bug had been fixed by making `OverlayPanel.OnMouseUp`'s right-click branch also raise `SurfaceInteracted` — in both the chip-hit and gutter sub-branches — BEFORE the `_router.Open*Menu(...)` call. That claim was wrong: the approach was tried, caused a live regression (the just-opened `ContextMenuStrip` self-closed, because destroying the hover dashboard posts activation fallout that only arrives after `OnMouseUp` returns, which `ToolStripManager.ModalMenuFilter` then reads as an activation change), and was reverted. The menu-dismissal bug's real root cause was unrelated to `SurfaceInteracted` entirely: `ContextMenuStrip.OnOpening` pre-sets `e.Cancel = true` whenever `Items.Count == 0`, before raising `Opening` to subscribers — the four menu builders rebuilt `Items` inside their own `Opening` handler without clearing that flag, so the first show of every freshly-built menu was silently refused. The actual fix is `MenuOpeningPolicy` plus each builder's `Opening` handler explicitly clearing `e.Cancel` after a successful rebuild. See [Hover Dashboard State Machine — Right-Click Does NOT Fire It (and Why That's a Constraint, Not a Gap)](hover-dashboard-state-machine.md#right-click-does-not-fire-it-and-why-thats-a-constraint-not-a-gap) for the full mechanism, the tried-and-reverted fix, and the code. Right-click does NOT fire `SurfaceInteracted` — it fires only on left-click — and `DragCompleted` never fires on right-click either.

**Design decision (captured during step 04b):** Rather than reusing `SurfaceInteracted` for drag completion, a new `DragCompleted` event was introduced. At the time, `SurfaceInteracted` only fired on left-click dispatch. Drag-drop repositioning does not dispatch an activation — adding it to `SurfaceInteracted` would have conflated "activation happened" with "surface interaction happened." The new event preserves that separation of concerns; it remains correct today, since `SurfaceInteracted` still fires only on left-click dispatch (the right-click-fires-it approach discussed above was tried and reverted), and `DragCompleted`'s narrower "a drag just finished" signal doesn't overlap with it either way.

**Subscription lifecycle (P6 — TrayApp owns all wiring):** Identical to `SurfaceInteracted` (subscribe at construction, unsubscribe/resubscribe on config reload, dispose on exit). `TrayApp.HandleOverlayDragCompleted()` calls `HandleSurfaceInteraction()` on both hover controllers — the same post-interaction cooldown (dwell suppression, form hide) that fires for click-to-activate.

**Implementation detail:** `DragCompleted` is wired at all four existing `SurfaceInteracted` subscription sites (initial subscribe, structural-reload teardown, structural-reload resubscribe, final `Dispose`), so lifecycle is uniform. Payload is `void` (no parameters); the handler only needs to know "drag just finished," not which session or workspace.

**Future guidance:** Any new overlay surface that needs "post-drag-completion" semantics should reuse `DragCompleted`, not add a third parallel event.
