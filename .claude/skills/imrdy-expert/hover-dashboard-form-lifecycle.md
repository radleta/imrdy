---
tags: [imrdy-expert/dashboard]
summary: "Non-layered DashboardForm: adaptive screen-aware anchoring + recreate-per-show for virtual desktop binding + IVirtualDesktopPinnedApps for persistence"
---

# Hover Dashboard Form Lifecycle

## The Challenge

A non-layered top-level WinForms `DashboardForm` must appear instantly on hover (200ms dwell) **on whichever virtual desktop the user is currently viewing**. Three problems collide:

1. **Virtual desktop binding:** Non-layered forms are automatically bound to the desktop they were created on. Once bound, the form is invisible on all other desktops.
2. **Geometry constraints:** The overlay defaults to the bottom of the screen, so a downward-tether form would be created off-screen. Screen awareness is required.
3. **Form persistence:** A form created on desktop 1 cannot be moved to desktop 2 via `IVirtualDesktopManager.MoveWindowToDesktop` (the stable documented API) — Windows silently ignores the request for non-layered, already-visible windows.

## Solution Architecture

### Three-Part Pattern

1. **Adaptive anchor**: Screen-aware positioning (below if fits, above otherwise)
2. **Recreate-per-show**: Dispose form on hide, create fresh on dwell (ensures desktop binding at creation time)
3. **IVirtualDesktopPinnedApps for persistence**: If the form must stay visible across desktops (not applicable to step-02 hover, but documented for future use)

## Part 1: Adaptive Anchor — Screen-Aware Positioning

A bottom-anchored overlay with downward tether breaks when the overlay sits near the bottom of the screen. Use `Screen.FromControl(_overlayWindow).WorkingArea` to detect monitor bounds and pick the anchor direction dynamically.

**Bad pattern (hard-coded below):**
```csharp
var overlayBounds = _overlayWindow.Bounds;
var formBounds = new Rectangle(
    overlayBounds.Left + 20,
    overlayBounds.Bottom + gap,  // ← Always below, even if off-screen
    width, height
);
```

**Good pattern (adaptive):**
```csharp
var screen = Screen.FromControl(_overlayWindow);
var workingArea = screen.WorkingArea;
var overlayBounds = _overlayWindow.Bounds;

// Try below first
var formBounds = new Rectangle(
    Math.Max(workingArea.Left, overlayBounds.Left + 20),
    overlayBounds.Bottom + gap,
    width, height
);

// Flip above if it doesn't fit
if (formBounds.Bottom > workingArea.Bottom)
{
    formBounds.Y = overlayBounds.Top - gap - height;
}

// Clamp X to working area (multi-monitor safety)
if (formBounds.Right > workingArea.Right)
{
    formBounds.X = workingArea.Right - width;
}
```

**Key details:**
- Use `Screen.FromControl(_overlayWindow)`, NOT `Screen.PrimaryScreen` — fails on multi-monitor setups where the overlay is on a secondary monitor.
- Clamp X because bottom-right overlay positions push the form toward the right edge; the centred X calculation can overflow.
- The grace-corridor geometry (`Rectangle.Union` + `BridgeGap` expansion) is agnostic to above/below — it works identically either way.

### Part 2: Recreate-Per-Show — Virtual Desktop Binding Strategy

**Attempted approach (FAILED):** Call `IVirtualDesktopManager.MoveWindowToDesktop` after `Show`.

```csharp
// Iter 7–8 approach — Windows silently ignores this for non-layered, shown windows
_form.Show();
_desktopManager.MoveWindowToCurrentDesktop(_form.Handle);  // ← S_OK but no-op
```

**Why it fails:** The documented COM API `IVirtualDesktopManager::MoveWindowToDesktop` (GUID `a5cd92ff-29be-454c-8d04-d82879fb3f1b`, slot 3) returns `S_OK` but Windows silently rejects the request for non-layered, already-visible top-level windows. The shell enforces desktop binding separately from the COM call's return value.

**Working approach: Recreate-per-show**

```csharp
private void TryShowForm()
{
    DisposeForm();  // Clean up old form if any
    _form = new DashboardForm(...);
    
    var screen = Screen.FromControl(_overlayWindow);
    var workingArea = screen.WorkingArea;
    var overlayBounds = _overlayWindow.Bounds;
    
    // Adaptive anchor geometry (see Part 1)
    var formBounds = ComputeFormBounds(overlayBounds, workingArea);
    
    _form.Bounds = formBounds;
    _form.Show();
    // Windows automatically binds this fresh top-level window to the current desktop
}

private void HideForm()
{
    DisposeForm();
}

private void DisposeForm()
{
    if (_form != null)
    {
        _form.Dispose();
        _form = null;
    }
}
```

**Why this is cheap enough:**
- DashboardForm is a lightweight non-layered WinForms Form.
- Even with full child-control layout (step 05), recreation is <50ms (acceptable within the 200ms dwell delay).
- No `MoveWindowToDesktop` call needed — Windows binds each new form to the current desktop at creation time automatically.

### Part 3: IVirtualDesktopPinnedApps for Persistence (Future Use)

Step 02 hover dismisses on click, so persistence is not needed. Documented here for future surfaces (tooltips, persistent sidebars).

**The API:**
```csharp
[ComImport]
[Guid("B5A399E7-1C87-46B8-88E9-FC5747B171BD")]  // CLSID_VirtualDesktopPinnedApps
private interface IVirtualDesktopPinnedApps
{
    int IsViewPinned(IntPtr view, out int isPinned);
    int PinView(IntPtr view);
    int UnpinView(IntPtr view);
}
```

**Critical: IApplicationView is IInspectable**

The `IApplicationView` interface type is `[InterfaceIsIInspectable]`. Do NOT try built-in marshaling on .NET 10 — use raw vtable dispatch instead (see `.NET 10: IInspectable Out-Parameter Marshaling Limitation` in com-interop-expert wiki). This caused iter 10 runtime failures.

**Stable GUIDs (Win10 1809 → Win11 24H2, no build-keying):**

| Name | GUID |
|---|---|
| `CLSID_VirtualDesktopPinnedApps` | `B5A399E7-1C87-46B8-88E9-FC5747B171BD` |
| `IID_IVirtualDesktopPinnedApps` | `4CE81583-1E4C-4632-A621-07A53543148F` |
| `IID_IApplicationViewCollection` | `1841C6D7-4F9D-42C0-AF41-8747538F10E5` |
| `IID_IApplicationView` | `372E1D3B-38D3-42E4-A15B-8AB2B178F513` |

**Pattern (not used in step 02, but if you need it):**
```csharp
// Acquire IApplicationView via raw vtable (see com-interop-expert wiki)
var viewPtr = GetApplicationViewForHwnd(_form.Handle);

// Pin it
var pinnedApps = GetVirtualDesktopPinnedAppsInterface();
var hr = pinnedApps.PinView(viewPtr);
if (hr >= 0)
{
    // Form is now visible on all desktops
}

Marshal.Release(viewPtr);
```

## Grace Corridor and Dismissal

The form is dismissed when:
1. Cursor leaves the grace corridor (expanded union of overlay bounds + form bounds) for `DwellResetDelay`
2. User clicks on the overlay icon (activates a session) — `InteractiveOverlayWindow.SurfaceInteracted` event fires (see [Hover Dashboard State Machine](hover-dashboard-state-machine.md))

The grace corridor geometry works identically for above/below anchoring — it's a simple `Rectangle.Union` with expansion.

## Related

- [Hover Dashboard State Machine](hover-dashboard-state-machine.md) — Dismiss logic and event patterns
- [Dev Build Marker & Logging](dev-build-marker-logging.md) — Debug logging for diagnostic traces during development
- [Overlay Interactivity](overlay-interactivity.md) (existing page) — Pass/Interactive window split and ISessionInteractionRouter
- [Sparkline Reference Time](sparkline-reference-time.md) — ReferenceTime anchor on SparklineControl for correct fixture-preview rendering
- [WinForms Custom Property Serialization](winforms-custom-property-serialization.md) — WFO1000 fix for SparklineControl.Timestamps and other non-serializable UserControl properties
