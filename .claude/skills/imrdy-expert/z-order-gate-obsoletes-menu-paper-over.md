---
tags: [imrdy-expert/overlay]
summary: "WindowFromPoint z-order gating makes overlay-hide-on-menu paper-overs redundant; pure geometric containment is insufficient"
---

## WindowFromPoint Z-Order Gate Obsoletes the Overlay-Hide-on-Menu Paper-Over

### Why the hide-on-menu logic existed

Before `WindowFromPoint` z-order gating was introduced, hover-intent detection used pure `Rectangle.Contains(cursor)` checks. When a tray context menu was open, the menu HWND physically covered the overlay row at the bottom of the screen, but the overlay's `Bounds` rectangle was unchanged — so cursor movement over the menu could still satisfy the geometric dwell condition and trigger a ghost dashboard show. The `_openTrayMenuCount` counter + `overlay.Visible = false` pattern was introduced as a paper-over: force the overlay off-screen for the menu's lifetime, restore on close.

### Why z-order gating makes it redundant

`HoverDashboardController.OnDrainTick` now calls `PInvokeOverlay.WindowAtPoint(cursor)` once per tick. The `cursorOverOverlay` boolean is `true` only when both conditions hold:

1. The cursor is geometrically inside the overlay's `Form.Bounds` rectangle.
2. `WindowFromPoint` returns the overlay's own HWND — meaning the overlay is the topmost window at that pixel.

When a tray context menu is open, the menu's HWND is above the overlay in Z-order and appears under the cursor. `WindowFromPoint` therefore returns the menu's HWND, not the overlay HWND. Condition 2 fails, `cursorOverOverlay` is false, and the dwell counter never increments. The dashboard cannot trigger while any foreign window covers the overlay row — the correctness guarantee the hide-on-menu logic was providing is now delivered by the z-order gate itself, with no extra state.

### Accepted trade-off

The old hide-on-menu code had a small secondary UX benefit: it removed the visual competition between the topmost overlay row and the topmost menu popup. Both sit near the screen bottom; a user who opens a tray menu would briefly see the overlay row underneath the open menu, which could look slightly cluttered. Removing the hide logic means the overlay remains visible while a menu is open. This is accepted in exchange for eliminating the `_openTrayMenuCount` state machine (field declaration + method `WireTrayMenuOpenCloseTracker` + `OnTrayMenuOpening` + `OnTrayMenuClosed` + three call sites), which was the only stateful coupling between the menu lifecycle and the overlay visibility path.

### Pattern generalization

Any hover-intent controller that gates on `WindowFromPoint == targetHwnd` inherits automatic immunity to foreign topmost windows (menus, system popups, Win+Tab, taskbar). Paper-over visibility toggles are only necessary when hover detection is purely geometric. Once z-order identity is in the gate, the paper-over adds complexity without adding correctness.

### When discovered

Step 08 iter-8 audit — Explore agent confirmed the hide-on-menu code was still present after z-order gating shipped.

### Impact

Future overlay-adjacent hover controllers in this project should rely on `WindowFromPoint` gating and skip any menu-lifecycle visibility coupling.
