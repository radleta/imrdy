---
tags: [imrdy-expert/desktop]
summary: "IVirtualDesktopManagerInternal: probe a newest-first IID candidate list and dispatch with the accepted IID's own vtable layout — never pick the IID by build number or key slots off 'is Windows 11'"
last-verified: "2026-09-26"
---

# COM Virtual Desktop Interop

imrdy switches virtual desktops through the undocumented `IVirtualDesktopManagerInternal`. The IID
and the vtable behind it change without notice, so read this before touching
[`VirtualDesktopGuids.cs` `GetInternalLayouts`](../../../src/Imrdy.Windows/Desktop/VirtualDesktopGuids.cs)
or [`ComVirtualDesktop.cs` `GetManagerInternal`](../../../src/Imrdy.Windows/Desktop/ComVirtualDesktop.cs).

## The build number cannot pick the IID

Windows servicing updates change the IID *within* a build number: 26200.9445 rejects `a3175f2d`
with `E_NOINTERFACE` and accepts `53f5ca0b` (commit 8bee7af). So `GetInternalLayouts(build)`
returns a newest-first list of `InternalLayout(Iid, HasMonitorArg, FindDesktopSlot)`, and
`GetManagerInternal` QueryServices each candidate in order and keeps the first one accepted.

To support a new servicing IID, add its entry at the head of the list for its build range, with
its own layout. Do not add a build range to carry a new IID.

## Each IID carries its own vtable layout

Take every signature and slot from the accepted entry. Never key them off "is Windows 11": on
`53f5ca0b`, slot 13 is `RemoveDesktop` and `FindDesktop` is slot 14, and the leading
`hWndOrMonitor` argument is gone, while `a3175f2d` and `b2f925b9` — also Windows 11 — have it and
put `FindDesktop` at 13 (commit 8bee7af removed the `IsWindows11` switch for this reason). A call
through a slot from the wrong layout invokes a different method. `SwitchDesktop` is slot 9 on
every layout.

## Diagnosing "desktop switching stopped after a Windows update"

With the dev-build marker on (see [Dev Build Marker & Logging](dev-build-marker-logging.md)), the
tray log carries one Debug line per candidate — `IID {Iid} accepted` or `QueryService rejected ...
(HRESULT ...)` — and one Warning only when every candidate is rejected. Then:

- **Every candidate rejected** → the build has a new IID. Desktop switching is off, but window
  queries through the documented `IVirtualDesktopManager` still work, so the tray keeps running.
- **Unknown build** (no range matches) → the candidate list is empty and the constructor logs
  `Unknown Windows build ... virtual desktop switching disabled`.
- **Worked, then failed after Explorer restarted** → a critical `COMException` triggers a lazy
  re-init; it is not a new IID.

Pinning a window to all desktops is a separate raw-vtable path with stable GUIDs; see
[Hover Dashboard Form Lifecycle](hover-dashboard-form-lifecycle.md#virtual-desktops). Which
desktop a click switches to is [WT Desktop Routing](wt-desktop-routing.md).
