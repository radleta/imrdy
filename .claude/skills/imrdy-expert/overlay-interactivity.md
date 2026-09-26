---
tags: [imrdy-expert/overlay]
summary: "Overlay input: only the grip arms a drag, the threshold is per-monitor-DPI, a drop snaps and persists per-monitor offsets, there is no click-through mode; DragCompleted and SurfaceInteracted carry separate contracts wired by TrayApp"
last-verified: "2026-09-26"
---

# Overlay Interactivity

## Grip Drag

[`OverlayPanel.cs` `IsGripHit`](../../../src/Imrdy.Windows/Overlay/OverlayPanel.cs) is the only
drag-arming test. `OnMouseDown` arms a drag only on the left grip handle (the 6-dot glyph,
`GripWidth` wide, dimmed until hovered) and only while `overlay.locked` is false. A chip or the
gutter never arms one: a chip click activates on `OnMouseUp`, and a grip or gutter click with no
drag does nothing.

- **The threshold is per-monitor DPI.** It is `PInvokeOverlay.GetSystemMetricForDpi(SM_CXDRAG /
  SM_CYDRAG, DeviceDpi)`, not `SystemInformation.DragSize`, which reads the system DPI and is wrong
  on a monitor whose DPI differs. Keep the per-DPI call if the threshold moves.
- **A drop snaps, clamps and persists per monitor.** The panel lands at the release point on the
  monitor under the cursor; [`OverlayPlacement.cs` `ComputeEdgeSnap`](../../../src/Imrdy.Core/Overlay/OverlayPlacement.cs)
  snaps it to a working-area edge or corner within 24 logical px, `ClampToWorkingArea` keeps it
  fully on-screen, and the result is written as `overlay.offsetX` / `overlay.offsetY` plus
  `overlay.monitor`. The placement fields behind that are on
  [overlay-rendering-internals](overlay-rendering-internals.md#monitor-and-position-placement).
- **A position preset writes an offset too.** The overlay menu's position presets resolve the anchor
  through `OverlayPlacement.AnchorToOffset` and write the offset, not a bare enum. An offset wins over
  `overlay.position`, so once one is set, editing `overlay.position` alone does not move the panel.
- **There is no click-through overlay.** `OverlayPanel` has no `WM_NCHITTEST` override and no
  passive variant. To get the overlay out of the way, set `overlay.enabled: false`.

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
