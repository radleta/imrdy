---
tags: [imrdy/overlay-interaction]
updated: 2026-04-16
summary: "Overlay interactivity: WS_EX_TRANSPARENT toggle, NCHITTEST hit-testing, selective click-through architecture"
---

# Overlay Interactivity Pattern

## The Core Problem: WS_EX_TRANSPARENT vs. Interaction

OverlayWindow's current design uses `WS_EX_TRANSPARENT` in CreateParams (line 32), which makes the entire window pass all mouse events through to windows underneath. This is essential for the current passive-read-only mode: clicks always reach the Claude Code terminal below.

**Adding interactivity requires a solution:** transparent pixels must still pass through, but rendered icon regions must intercept clicks and trigger session focus/activation.

## Solution: Runtime Toggle + Hit-Test Override

### 1. Runtime Toggle (No Window Reconstruction)

`WS_EX_TRANSPARENT` can be toggled at runtime via Win32 without recreating the window:

```csharp
private const int GWL_EXSTYLE = -20;
private const uint WS_EX_TRANSPARENT = 0x00000020;
private const uint SWP_NOMOVE = 0x0002;
private const uint SWP_NOSIZE = 0x0001;
private const uint SWP_NOZORDER = 0x0004;
private const uint SWP_FRAMECHANGED = 0x0020;

// Toggle transparent mode
IntPtr hwnd = Handle;
uint current = GetWindowLong(hwnd, GWL_EXSTYLE);
SetWindowLong(hwnd, GWL_EXSTYLE, current ^ WS_EX_TRANSPARENT);
SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 
             SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
```

**Key advantage:** `WS_EX_LAYERED` + per-pixel alpha (UpdateLayeredWindow) continue to work perfectly. No re-rendering, no bitmap recreation — just a style flag toggle.

### 2. Hit-Test Override (Critical)

When `WS_EX_TRANSPARENT` is disabled, Windows will send mouse messages, but only if `WM_NCHITTEST` returns `HTCLIENT`. Override `WM_NCHITTEST` to implement selective pass-through:

```csharp
protected override void WndProc(ref Message m)
{
    const int WM_NCHITTEST = 0x0084;
    
    if (m.Msg == WM_NCHITTEST)
    {
        // Extract mouse coordinates (already in screen space)
        int mouseX = (int)m.LParam & 0xFFFF;
        int mouseY = ((int)m.LParam >> 16) & 0xFFFF;
        
        // Convert to window-local coordinates
        Rectangle bounds = Bounds;
        int localX = mouseX - bounds.Left;
        int localY = mouseY - bounds.Top;
        
        // Check if within any icon rectangle
        if (HitTestIcon(localX, localY))
        {
            m.Result = (IntPtr)2;  // HTCLIENT — intercept this click
            return;
        }
        
        // Gaps between icons: pass through
        m.Result = (IntPtr)(-1);  // HTTRANSPARENT — let it through
        return;
    }
    
    base.WndProc(ref m);
}

private bool HitTestIcon(int localX, int localY)
{
    // Pure integer math using known layout: 
    // iconSize = 16, spacing = 4, so each slot is 20 pixels wide
    int slotWidth = IconSize + Spacing;  // e.g., 20
    
    // Which icon column?
    int iconIndex = localX / slotWidth;
    int inIconX = localX % slotWidth;
    
    // Verify within bounds and within the icon width (not the spacing gap)
    if (iconIndex < SessionCount && inIconX < IconSize)
    {
        // Icon rectangle hit
        return true;
    }
    
    return false;
}
```

**Why NCHITTEST matters:** Windows uses the return value of `WM_NCHITTEST` to decide whether to deliver mouse messages _at all_. Only `HTCLIENT` causes mouse events (WM_LBUTTONDOWN, etc.) to be delivered. `HTTRANSPARENT` tells Windows "this isn't part of my window" and passes the click through to windows behind.

### 3. Hit-Test Math (Not Alpha-Channel)

Use **rectangle math, not per-pixel alpha testing**:

- Icons render at known positions: `x = iconIndex * (IconSize + Spacing)`, `y = margin`
- Modulo math to detect icon boundaries: `inIconX = mouseX % slotWidth`
- Zero per-pixel overhead — pure integer comparison

**Avoid:** Testing the icon bitmap's alpha channel at the mouse point. Anti-aliased edges and rendering artifacts make alpha-based hit-testing unreliable. The overlay's icon positions are deterministic; use that.

## State Preservation During Toggle

When switching from transparent → interactive or back:

1. **Icon bitmaps:** Unaffected — already in the layered bitmap, kept via UpdateLayeredWindow
2. **Color aging:** Continue normally — the overlay rendering loop doesn't change
3. **Session list:** Continue rendering on drain timer — click handling is additive

Toggle can happen in response to user config or session state without full re-render.

## Related

- [Architecture](architecture.md) — Overlay rendering loop, timer interactions
- [Status Mapping](status-mapping.md) — Icon color/aging by status
