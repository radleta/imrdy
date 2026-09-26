---
tags: [imrdy-expert/overlay]
summary: "DragCompleted event fires at end of drag-to-reposition in OnMouseUp; companion to SurfaceInteracted with separate contract; subscription lifecycle identical (P6 TrayApp owns wiring)"
last-verified: "2026-09-25"
---

## DragCompleted Event

`OverlayPanel` exposes two parameterless events, each with a narrow contract:

| Event | Raised when | Not raised |
|---|---|---|
| `SurfaceInteracted` | a left-click has dispatched an activation through the router | on right-click (either sub-branch), on a drag, on a router exception |
| `DragCompleted` | the grip-drag branch of `OnMouseUp` completes a reposition, before `ResetDragState()` | on any click, left or right |

Right-click raises neither. Why right-click must not raise `SurfaceInteracted` is in [Hover Dashboard State Machine — Right-Click Does NOT Fire It (and Why That's a Constraint, Not a Gap)](hover-dashboard-state-machine.md#right-click-does-not-fire-it-and-why-thats-a-constraint-not-a-gap).

**Why a second event rather than reusing `SurfaceInteracted`:** a drag-drop dispatches no activation. Folding it into `SurfaceInteracted` would conflate "an activation happened" with "the surface was touched." Any new overlay surface that needs post-drag semantics should reuse `DragCompleted`, not add a third parallel event.

**Subscription lifecycle (P6 — TrayApp owns all wiring):** identical to `SurfaceInteracted` — subscribed right after overlay construction, unsubscribed and resubscribed around `OnConfigChanged`'s structural overlay recreate, and unsubscribed at shutdown. `DragCompleted` is wired at every `SurfaceInteracted` subscription site so the two cannot drift apart. `TrayApp.HandleOverlayDragCompleted()` calls `HandleSurfaceInteraction()` on both hover controllers — the same post-interaction cooldown (form hide, dwell suppressed until the cursor leaves the overlay) that a click-to-activate gets.

A right-click that opens a menu relies on neither event; how that menu gets and returns foreground is in [Overlay Context Menus](overlay-context-menus.md).
