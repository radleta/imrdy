---
tags: [imrdy-expert/dashboard]
summary: "Detect session change via TryGetSessionIdAtScreenPoint while form is visible; apply live-update pattern"
---

## Hover-Preview Live-Switch: Detect Session Change While Form Is Visible

When a stable hover-preview form stays open as the user moves the cursor across multiple trigger targets (overlay session icons), the form-visible branch of the drain-tick must actively poll `TryGetSessionIdAtScreenPoint` on every tick where the cursor is inside the overlay bounds. Without this poll the `_hoveredSessionId` set during the initial `TryShowForm` never updates, so the form keeps showing stale data from session A even as the cursor moves to session B.

### Pattern: live-switch check inside the grace-corridor's in-bound branch

```csharp
if (overlayBounds.Contains(cursor) &&
    _overlayWindow.TryGetSessionIdAtScreenPoint(cursor, out var nowHoveredId) &&
    nowHoveredId != _hoveredSessionId)
{
    _hoveredSessionId = nowHoveredId;
    RebuildAndApplyUpdate(nowHoveredId);  // data refresh only, no re-pin, no opacity reset
}
```

### Key implementation notes

- Only call `TryGetSessionIdAtScreenPoint` when `overlayBounds.Contains(cursor)` is already true — the cursor-gap case (between icons, returns false) is safely handled by the null-check on the `out` variable.
- Do NOT recreate the form, re-pin to all desktops, or reset opacity. `Update(vm)` applies data changes to the existing visible form in place.
- If git info is not cached for the new session, kick off the same async `Task.Run` + `BeginInvoke` fetch path used in `TryShowForm`. Guard the async continuation with `if (_hoveredSessionId != sessionId) return;` to discard results that arrive after the user has moved to yet another session.
- Update `_hoveredSessionId` BEFORE calling `RebuildAndApplyUpdate` so any racing async continuations see the correct current session immediately.

### When discovered

Step 08 iter-3 — user reported "only shows data from initial session I hover instead of switching data between as I hover different sessions" during the manual gate.

### Impact

Any hover-preview pattern where the user can traverse multiple triggers while a single form stays open (overlay icon row, icon grid, etc.) — session-switch detection is mandatory, not optional.
