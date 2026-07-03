---
tags: [imrdy/overlay-geometry]
summary: "OverlayPlacement applies bottom taskbar reserve unconditionally, unlike the original CalculatePosition"
code-cites: []
---

## Discovered Behavior: Unconditional Bottom Taskbar Reserve

The new `OverlayPlacement.ResolveOrigin` and `AnchorToOffset` functions apply an 8px bottom taskbar reserve unconditionally when resolving to a Bottom-anchored position. This differs from the original `OverlayPanel.CalculatePosition`, which applied the reserve conditionally.

### Original Conditional Logic

The existing `CalculatePosition` checks whether the working area already reserves space for an auto-hide taskbar:

```csharp
var taskbarReserve = wa == screen.Bounds ? 8 : 0;
```

If `WorkingArea == Bounds` (no space already reserved), it adds 8px. Otherwise, it assumes the taskbar is already accounted for in `WorkingArea`.

### New Unconditional Logic

The Core functions take only `Rectangle workingArea` as input—no separate `screenBounds` parameter—so they cannot replicate the conditional check. The 8px bottom reserve is applied unconditionally on every Bottom-anchored resolution, regardless of whether the monitor's taskbar is already excluded from `WorkingArea`.

### Impact and Next Steps

**On monitors with non-auto-hide taskbars** (where `WorkingArea != Bounds`), the ported logic reserves an extra 8px gap above the taskbar that the original code skipped. This is typically unnoticeable visually but may appear as unwanted spacing on some systems.

**Resolution options for Step 02:**
1. Accept the always-8px behavior as an intentional simplification
2. Widen `OverlayPlacement`'s signature to accept `screenBounds` explicitly for a conditional check
3. Verify via `imrdy render` and live-tray testing; if the gap is visible and objectionable, implement option 2

### References

- Original code: `src/Imrdy.Windows/Overlay/OverlayPanel.cs` `CalculatePosition` (~L740–763, research.md)
- New functions: `src/Imrdy.Core/Overlay/OverlayPlacement.cs` `ResolveOrigin` / `AnchorToOffset`
- Related plan step: Step 02 (OverlayPanel offset plumbing + live-reload wiring) must decide the approach
