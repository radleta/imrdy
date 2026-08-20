---
tags: [imrdy-expert/overlay-geometry]
summary: "OverlayPlacement applies bottom taskbar reserve unconditionally, unlike the original CalculatePosition"
code-cites: []
---

## Discovered Behavior: Unconditional Bottom Taskbar Reserve

The new `OverlayPlacement.ResolveOrigin` and `AnchorToOffset` functions apply an 8px bottom taskbar reserve unconditionally when resolving to a Bottom-anchored position. This differs from the original `OverlayPanel.CalculatePosition`, which applied the reserve conditionally.

### Original Conditional Logic (pre-free-float, now removed)

`OverlayPanel.CalculatePosition` used to check whether the working area already reserves space for an auto-hide taskbar:

```csharp
var taskbarReserve = wa == screen.Bounds ? 8 : 0;
```

If `WorkingArea == Bounds` (no space already reserved), it added 8px. Otherwise, it assumed the taskbar was already accounted for in `WorkingArea`. This conditional no longer exists anywhere in source — `CalculatePosition` now just delegates to `OverlayPlacement.ResolveOrigin`.

### Current Unconditional Logic (shipped)

`OverlayPlacement.ResolveAnchorOrigin` (the shared anchor→origin math backing both `ResolveOrigin`'s anchor-fallback branch and `AnchorToOffset`) takes only `Rectangle workingArea` as input — no separate `screenBounds` parameter — so it cannot replicate the conditional check. The 8px bottom reserve (`OverlayPlacement.BottomTaskbarReserve`) is applied unconditionally on every Bottom-anchored resolution, regardless of whether the monitor's taskbar is already excluded from `WorkingArea`.

### Resolution

Option 1 ("accept the always-8px behavior as an intentional simplification") was the one shipped — `OverlayPlacement`'s signature was never widened to accept `screenBounds`. On monitors with non-auto-hide taskbars (where `WorkingArea != Bounds`), the panel reserves an extra 8px gap above the taskbar that the original conditional code would have skipped; this has not surfaced as a visible/objectionable issue in `imrdy render` or live-tray testing.

### References

- Current logic: `src/Imrdy.Core/Overlay/OverlayPlacement.cs` `ResolveOrigin` / `ResolveAnchorOrigin` / `AnchorToOffset`
- `OverlayPanel.CalculatePosition` (`src/Imrdy.Windows/Overlay/OverlayPanel.cs`) is now a thin wrapper over `OverlayPlacement.ResolveOrigin`
