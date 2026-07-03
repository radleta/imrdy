---
tags: [imrdy-expert/overlay]
summary: "DragCompleted event fires at end of drag-to-reposition in OnMouseUp; companion to SurfaceInteracted with separate contract; subscription lifecycle identical (P6 TrayApp owns wiring)"
code-cites: []
---

## DragCompleted Event

`OverlayPanel` exposes `DragCompleted` (fires at the end of the drag-to-reposition branch in `OnMouseUp`, before `ResetDragState()`). This event complements `SurfaceInteracted` — right-click does NOT fire either event.

**Design decision (captured during step 04b):** Rather than reusing `SurfaceInteracted` for drag completion, a new `DragCompleted` event was introduced. `SurfaceInteracted`'s documented contract is "fires only after a left-click dispatches a session/workspace activation; right-click never fires it." Drag-drop repositioning does not dispatch an activation — adding it to `SurfaceInteracted` would have silently broken that documented contract for any future reader. The new event preserves the separation of concerns.

**Subscription lifecycle (P6 — TrayApp owns all wiring):** Identical to `SurfaceInteracted` (subscribe at construction, unsubscribe/resubscribe on config reload, dispose on exit). `TrayApp.HandleOverlayDragCompleted()` calls `HandleSurfaceInteraction()` on both hover controllers — the same post-interaction cooldown (dwell suppression, form hide) that fires for click-to-activate.

**Implementation detail:** `DragCompleted` is wired at all four existing `SurfaceInteracted` subscription sites (initial subscribe, structural-reload teardown, structural-reload resubscribe, final `Dispose`), so lifecycle is uniform. Payload is `void` (no parameters); the handler only needs to know "drag just finished," not which session or workspace.

**Future guidance:** Any new overlay surface that needs "post-drag-completion" semantics should reuse `DragCompleted`, not add a third parallel event.
