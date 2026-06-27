---
tags: [imrdy-expert/dashboard]
summary: "HoverDashboardControllerBase owns the dwell/grace state machine; derived controllers plug in domain-specific dispatch (TryHitTestForOurDomain → BuildViewModel → CreateForm → ShowForm → ApplyViewModelUpdate); cross-controller hide protocol via FormShown event wired in TrayApp"
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
| `ApplyViewModelUpdate(form, viewModel)` | Call the typed `form.Update(TViewModel)` overload. Used by the switch-detection path (cursor moved from item A to item B of the same domain while form was already visible). |

Extension points called by the base state machine:
- `OnSameItemRefreshTick(currentItem)` — called every `RefreshIntervalTicks=10` (~1s) while form is visible on the same item. `SessionHoverDashboardController` overrides to call `RebuildAndApplyUpdate`; `WorkspaceHoverDashboardController` overrides to rebuild VM with fresh `DateTimeOffset.UtcNow` so `ActivityText` advances.
- `OnFormShown(item, viewModel, cursor)` — called after the form is shown and pinned. `SessionHoverDashboardController` uses it to kick off async git fetch.
- `OnFormHidden()` — called when form hides. `SessionHoverDashboardController` uses it to null `_hoveredSessionId`.

### Workspace→Workspace Switch-Detection Requirement

When the user traverses from workspace icon A to workspace icon B while the dashboard is already visible, the base fires `ApplyViewModelUpdate(form, newVm)`. This requires `WorkspaceDashboardForm.Update(vm)` to refresh **all** dynamic fields — not just `_activityLabel`. If any field is missed, the dashboard shows stale data for workspace A. The field-promote pattern (`winforms-update-field-promote.md`) is the guard: every dynamic control must be a class field so `Update(vm)` can reach it.

## Cross-Controller Hide Protocol

Two controllers run simultaneously (session + workspace). Only one dashboard should be visible at a time. The protocol:

1. `HoverDashboardControllerBase.FormShown` event — raised at the end of `TryShowForm`, after `OnFormShown` returns.
2. `HideIfVisible()` — idempotent method on each controller; triggers the existing fade-out animation if a form is currently shown. No-op when already hidden or already dismissing (`_opacityDirection == -1`).
3. TrayApp wires the cross-subscribe (P6 — wiring NOT in base ctor):
   ```csharp
   _sessionController.FormShown  += () => _workspaceController.HideIfVisible();
   _workspaceController.FormShown += () => _sessionController.HideIfVisible();
   ```

**Why wiring belongs in TrayApp:** the base ctor must not subscribe to peers because it doesn't know who its peer is. Subscribing from a derived ctor creates a coupling between peers that is invisible at the call site. TrayApp is the single place that knows both controllers exist — it is the canonical subscription site.

**Anti-pattern**: controllers discovering peers via a shared registry and self-wiring on construction. This makes the hide protocol implicit and breaks when controllers are replaced (e.g., `OnConfigChanged`).

## The Problem (Original — SurfaceInteracted Event)

A hover-preview controller that uses a grace corridor for cursor continuity between overlay icon and form creates an interaction bug. After a click activation:

1. Form becomes visible
2. Every drain tick finds `_form.Visible == true`, resets `_dwellTicks = 0`
3. Grace corridor remains "active" — any new hover resets the countdown
4. Cursor over a different icon is silently swallowed until the corridor expires (typically 150-300ms)
5. User clicks icon A, clicks icon B immediately, sees icon A's dashboard instead (bad UX)

**Root cause:** The controller doesn't distinguish between:
- **Cursor traversal** — user moving mouse through the corridor between clicks (should tolerate pauses)
- **User commitment** — user clicked an icon and expects a state change (should dismiss immediately)

## Solution: SurfaceInteracted Event

Add an event that fires after successful **surface action** (left-click dispatch). The controller listens and dismisses immediately, bypassing the grace corridor timer.

### Implementation Pattern

**1. Overlay fires event after successful dispatch:**

```csharp
// In OverlayPanel
protected override void OnMouseUp(MouseEventArgs e)
{
    try
    {
        if (e.Button == MouseButtons.Left && HitIconIndex(e.X, out var idx))
        {
            var item = _items[idx];
            var anchor = MenuAnchor.AtControl(this, e.Location);
            if (item.ItemType == DisplayItemType.Session)
                _router.ActivateSession(item.Id);  // May switch desktop
            else
                _router.ActivateWorkspace(item.Id);
            
            // Fire AFTER successful dispatch (inside try, before catch)
            SurfaceInteracted?.Invoke(this, EventArgs.Empty);
        }
    }
    catch (Exception ex)
    {
        _logger.LogError("Overlay click failed: {Message}", ex.Message);
        // Note: SurfaceInteracted does NOT fire on exception
    }
    base.OnMouseUp(e);
}

public event EventHandler? SurfaceInteracted;
```

**Key details:**
- Event fires **inside the try block, after router call** — router exceptions don't trigger spurious dismissal
- Right-click does NOT fire the event (right-click shows a menu; WinForms handles menu dismissal naturally)

**2. Controller listens and dismisses:**

```csharp
// In HoverDashboardController
public void HandleSurfaceInteraction()
{
    HideForm();  // Same reset path as grace-corridor expiry
}
```

**3. Wire in TrayApp, not in the controller:**

```csharp
// In TrayApp constructor / OnConfigChanged
var hoverController = new HoverDashboardController(...);
var overlay = _overlayWindow;

// Subscribe AFTER overlay is constructed
overlay.SurfaceInteracted += (s, e) => hoverController.HandleSurfaceInteraction();

// On config change: unsubscribe from OLD overlay before disposing
var oldOverlay = _overlayWindow;
if (oldOverlay != null)
{
    oldOverlay.SurfaceInteracted -= ...;  // ← Capture old reference first
}
_overlayWindow = null;
// Create new overlay and re-subscribe
```

**Subscription lifecycle safety:**
- Subscribe immediately after overlay construction
- Capture the **old** overlay reference before nulling it (to unsubscribe correctly)
- Unsubscribe before disposing controller or overlay in `OnConfigChanged` and `ExitThreadCore`

## State Machine Diagram

```
[Idle]
  ↓ (hover on icon, dwell=0)
[Hovering] ← dwell tick increments
  ↓ (dwell >= DwellThreshold)
[Showing] ← TryShowForm() fires
  │
  ├─ (cursor in grace corridor)
  │   ↓ (stays in corridor)
  │   [Showing] ← dwellTicks stays 0 (not reset, stays visible)
  │
  ├─ (cursor leaves corridor)
  │   ↓ (corridor expiry)
  │   [Hiding] ← HideForm() fires
  │
  └─ (SurfaceInteracted event fires)
      ↓ (user clicked icon)
      [Hiding] ← HideForm() fires immediately
```

## Grace Corridor Geometry

The corridor is a `Rectangle.Union` of overlay bounds and form bounds, expanded by `BridgeGap`. It's **independent of anchor direction** (above/below) — the union calculation works the same way.

```csharp
private bool IsCursorInCorridor(int x, int y)
{
    var corridor = Rectangle.Union(_overlayWindow.Bounds, _form.Bounds);
    corridor.Inflate(BridgeGap, BridgeGap);
    return corridor.Contains(x, y);
}
```

When the form is hidden, the corridor reverts to the overlay bounds alone (no expansion).

## Drain Tick Logic

The drain loop runs every 100ms and updates dwell state:

```csharp
public void Drain(DateTime now)
{
    if (_form == null)
    {
        // Form is hidden
        if (IsHoverActive && IsCursorInOverlay())
        {
            _dwellTicks++;
            if (_dwellTicks >= DwellThreshold)
            {
                TryShowForm();
            }
        }
        else
        {
            _dwellTicks = 0;  // Reset if cursor leaves overlay
        }
    }
    else if (_form.Visible)
    {
        // Form is visible — grace corridor extended bounds
        if (IsCursorInCorridor(cursorX, cursorY))
        {
            _dwellTicks = 0;  // STAYS showing, no increment
        }
        else
        {
            _dwellTicks++;
            if (_dwellTicks >= CorridorExpiryDelay)
            {
                HideForm();  // Corridor expired
            }
        }
    }
}
```

**Key behavior:**
- When the form is shown, `_dwellTicks` does NOT increment while cursor is in the corridor (dwell is "frozen")
- When the form is shown and cursor leaves the corridor, dwell increments and fires a hide
- `SurfaceInteracted` event fires from the overlay click handler, invoking `HideForm()` immediately (no dwell wait)

## Anti-Patterns

| Anti-Pattern | Why Wrong | Fix |
|---|---|---|
| "Dismissing on dwell expiry is good enough" | Doesn't handle immediate click-to-different-icon | Add SurfaceInteracted event for commit path |
| "Subscribing in the controller" | Couples controller to overlay implementation | Subscribe in TrayApp; controller only knows about form visibility state |
| "Right-click fires SurfaceInteracted" | Right-click shows menu; WinForms dismissal is automatic | Right-click must NOT fire the event |
| "Grace corridor resets dwell to 0" | Dwell should be frozen (showing), not reset | When in corridor, dwell stays at DwellThreshold (show); when leaving, it increments toward expiry |

## Post-Interaction Cooldown

### The Ghost Re-Show Problem

After a user clicks an overlay icon to activate a session, the cursor may physically remain inside the hover-trigger bounds (the overlay row). When `HandleSurfaceInteraction` calls `HideForm()`, the dwell accumulator **does not** reset — it stays at 0. On the **very next drain tick** (100–200ms later), the `Drain` method re-evaluates:

1. Form is now hidden
2. Cursor is still in overlay
3. Dwell increments: `_dwellTicks++`
4. If `_dwellTicks >= DwellThreshold` (typically 2–3 ticks), `TryShowForm()` fires
5. Dashboard reappears for the session the user just clicked away from

This is a UX bug: the user committed an action (click = session activation) and the UI should respect that commitment until they move the cursor away and voluntarily hover again.

### Solution: `_awaitingOverlayExit` Flag

Add a boolean flag set true when `HandleSurfaceInteraction` fires, cleared only when the drain tick observes the cursor has physically left the overlay bounds.

**Implementation:**

```csharp
private bool _awaitingOverlayExit = false;

public void HandleSurfaceInteraction()
{
    _awaitingOverlayExit = true;  // ← Set flag
    HideForm();
    _logger.LogDebug("post-interaction cooldown set — dwell suppressed until cursor exits overlay");
}

public void Drain(DateTime now)
{
    // Early exit: if awaiting overlay exit, suppress dwell accumulation entirely
    if (_awaitingOverlayExit)
    {
        if (!IsCursorInOverlay())
        {
            _awaitingOverlayExit = false;
            _dwellTicks = 0;
            _logger.LogDebug("cursor exited overlay; post-interaction cooldown lifted");
        }
        return;  // ← Skip all dwell logic until flag clears
    }
    
    // Rest of drain logic (form hidden, form visible, etc.)
    // ...
}
```

**Key behavior:**
- When the flag is set, `Drain()` returns early and skips dwell accumulation entirely
- The flag stays set as long as `IsCursorInOverlay() == true`
- As soon as the cursor physically leaves the overlay, the flag clears and dwell resumes normally
- Typical gap between click and flag clearance: **8–43ms** (natural cursor motion after a click quickly exits the icon row)

### Distinction From Grace Corridor

The grace corridor and post-interaction cooldown serve **different lifecycle transitions**:

| Feature | Grace Corridor | Post-Interaction Cooldown |
|---|---|---|
| **Active when** | Form is visible | Form is hidden, after user clicked |
| **Purpose** | Tolerate cursor drift between overlay icon and dashboard form | Prevent dwell re-trigger after user commitment |
| **Duration** | ~150–300ms (until cursor leaves expanded bounds) | ~8–43ms (until cursor naturally exits overlay row) |
| **Trigger** | Hover dwell threshold reached | SurfaceInteracted event (overlay click) |
| **Exit condition** | Cursor leaves grace corridor bounds | Cursor leaves overlay bounds |

Both are per-session-instance flags/counters that protect the preview lifecycle — grace corridor shields the showing→hiding transition, post-interaction cooldown shields the hiding→ready-to-show transition.

### Related

- [Hover Dashboard Form Lifecycle](hover-dashboard-form-lifecycle.md) — Adaptive anchor and recreate-per-show strategy
- [Overlay Interactivity](overlay-interactivity.md) (existing page) — ISessionInteractionRouter contract and MenuAnchor dispatch
