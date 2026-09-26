---
tags: [imrdy-expert/dashboard]
summary: "HoverDashboardFormBase owns the shared shell (rounded Region, focus guard, pin/unpin, anchor placement, all-desktops pinning); derived forms (SessionDashboardForm, WorkspaceDashboardForm) own their content panels — field-promote all dynamic controls for Update(vm) access"
last-verified: "2026-09-25"
---

# Hover Dashboard Form Lifecycle

## Base / Derived Split

`HoverDashboardFormBase` (abstract `Form`) owns the shared shell that both dashboard peers need:

- `FormBorderStyle.None`, `TopMost=true`, `ShowInTaskbar=false`
- No DWM mica backdrop — dashboards fade via `Form.Opacity` (layered window); mica on a layered form composites white into GDI `Region`-carved corners. `ImrdyPalette.ApplyMica` is NOT called from `OnHandleCreated`. Overlay uses DWM mica; dashboards do not.
- Rounded `Region` clip (radius 14)
- `WM_MOUSEACTIVATE` focus guard (`MA_NOACTIVATE` when unpinned / `MA_ACTIVATE` when pinned)
- `ShowWithoutActivation => true`, so `Show()` never activates the form and disposing a shown dashboard never changes the OS active window (see [Hover Dashboard State Machine](hover-dashboard-state-machine.md) for the menu it would otherwise close)
- `Pin()` / `Unpin()` / `IsPinned` API — two-click pin-then-activate invariant
- Escape key handler — `OnKeyDown` unpins + hides
- Screen-aware anchor-edge placement (`ComputeAnchorPlacement` → `PlaceWithAnchor`)
- `PinAcrossVirtualDesktops()` — pins the form to every virtual desktop
- `FormatDuration` — thin delegating wrapper to `RelativeTimeFormatter` in `Imrdy.Core.Time`
- Palette colors (`BgForm`, `FgPrimary`, `FgSecondary`, `FgMuted`, `BgFooter`) sourced from `ImrdyPalette` (`src/Imrdy.Windows/Theme/`); `BridgeGap=12` as `protected const`
- `FormMinWidth = 520` declared on base so derived forms seed inner widths consistently

Derived classes own their **content panel only**:
- `SessionDashboardForm` — chip strip, sparkline, git footer, fleet strip
- `WorkspaceDashboardForm` — header (Name + Desktop), activity row, conditional git row, footer

**Field-promote pattern**: every control whose text/visibility/colors change per-VM must be declared as a class field. `Update(vm)` is the sole content source — locals inside helper methods are unreachable from `Update`. See [WinForms Update Field-Promote](winforms-update-field-promote.md).

**Conditional rows**: use `SetRowVisible(rowIndex, visible, height)` to toggle `TableLayoutPanel.RowStyle.Height` between 0 (hidden) and the normal height (visible). Both `SessionDashboardForm` and `WorkspaceDashboardForm` use this pattern.

**BuildLayout / Update split**: ctor calls `BuildLayout()` (VM-agnostic skeleton: create controls, add to layout, wire fonts/colors) then `Update(vm)` (sole content source: assign text, set visibility, rebuild chip lists). On each VM refresh, only `Update(vm)` is called — no re-layout.

## Show Sequence

`HoverDashboardControllerBase.TryShowForm` builds the view model, then:

1. **Recreate** — `DisposeForm()` on any previous form, then `CreateForm(viewModel)`. A dashboard is never reused across shows.
2. **Place** — `ComputeAnchorPlacement(overlayBounds, cursor, workingArea)` then `PlaceWithAnchor`.
3. **Fade in** — `Opacity = 0`, then `ShowForm`; the drain tick steps opacity by 0.5 per tick.
4. **Pin to all desktops** — `PinAcrossVirtualDesktops()`.
5. `OnFormShown`, then the `FormShown` event (the cross-controller hide protocol).

Hiding fades out the same way and disposes the form when opacity reaches 0; `ForceHideForm` disposes immediately.

## Placement

`ComputeAnchorPlacement` uses the working area of `Screen.FromControl(_overlayWindow)` — never `Screen.PrimaryScreen`, which is wrong when the overlay sits on a secondary monitor.

- **Y:** below the overlay (`BridgeGap` under its bottom edge) when the form fits there, else above it; when neither fits, whichever side has more room.
- **X:** the form's span is kept inside the overlay's span, sliding toward the cursor within that range; when the form is wider than the overlay it is centred over the overlay. A final clamp keeps it inside the working area. Do NOT centre on the cursor X — the form width is fixed, so for an edge-docked overlay that pins the popup to the screen edge.

The grace-corridor geometry (`Rectangle.Union` + `BridgeGap` inflate) is agnostic to above/below.

## Virtual Desktops

A shown dashboard has to appear on whichever virtual desktop the user is on. Two mechanisms carry that:

- **Recreate-per-show.** A fresh top-level window is created on the current desktop. Moving an existing shown window with the documented `IVirtualDesktopManager::MoveWindowToDesktop` was tried and returned `S_OK` without moving it, so no code path relies on it.
- **Pin to all desktops.** `PinAcrossVirtualDesktops` calls `IDesktopManager.PinWindowToAllDesktops(Handle)`, which pins the window's `IApplicationView` through `IVirtualDesktopPinnedApps`. It is a no-op when the form has no desktop manager — headless callers (`imrdy render`, fixtures) pass `null`.

Pinning uses raw vtable dispatch (`UnmanagedFunctionPointer` delegates, `IApplicationView` as an opaque `IntPtr`), not a `[ComImport]` interface: `IApplicationView` is an `IInspectable` interface that .NET 10's built-in COM marshaling does not handle as an out-parameter, and the `IApplicationViewCollection` slot has to be located at runtime. The GUIDs live in `ComVirtualDesktop`'s `PinningGuids`:

| Name | GUID |
|---|---|
| `CLSID_VirtualDesktopPinnedApps` | `B5A399E7-1C87-46B8-88E9-FC5747B171BD` |
| `IID_IVirtualDesktopPinnedApps` | `4CE81583-1E4C-4632-A621-07A53543148F` |
| `IID_IApplicationViewCollection` | `1841C6D7-4F9D-42C0-AF41-8747538F10E5` |

## Grace Corridor and Dismissal

The form is dismissed when:
1. The cursor stays outside the grace corridor (overlay ∪ form, inflated by `BridgeGap`) for `DismissThresholdTicks`
2. The user left-clicks an overlay chip — `OverlayPanel.SurfaceInteracted` fires
3. The peer dashboard shows — `HideIfVisible` via the `FormShown` protocol

See [Hover Dashboard State Machine](hover-dashboard-state-machine.md) for all three.

## Related

- [Hover Dashboard State Machine](hover-dashboard-state-machine.md) — dwell, corridor, dismissal and the cross-controller protocol
- [Dev Build Marker & Logging](dev-build-marker-logging.md) — Debug logging for diagnostic traces during development
- [Overlay Interactivity](overlay-interactivity.md) — the overlay's `SurfaceInteracted` and `DragCompleted` events
- [Sparkline Reference Time](sparkline-reference-time.md) — ReferenceTime anchor on SparklineControl for correct fixture-preview rendering
- [WinForms Custom Property Serialization](winforms-custom-property-serialization.md) — WFO1000 fix for SparklineControl.Timestamps and other non-serializable UserControl properties
