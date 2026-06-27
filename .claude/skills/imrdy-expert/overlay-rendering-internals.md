---
tags: [imrdy-expert/overlay]
summary: "OverlayPanel OnPaint rendering; bitmap cache keyed by (style,status); aging via chip-background opacity ladder in OnPaint; Form.Bounds reliability on non-layered forms; monitor placement via OverlayConfig.Monitor"
code-cites:
  - src/Imrdy.Windows/Overlay/OverlayPanel.cs
  - src/Imrdy.Windows/Desktop/PInvokeOverlay.cs
  - src/Imrdy.Core/Status/StatusMap.cs
---

# Overlay Rendering Internals

## Overview

`OverlayPanel` (`src/Imrdy.Windows/Overlay/OverlayPanel.cs`) is a non-layered WinForms `Form` that renders via `OnPaint`. It replaces the former `OverlayWindowBase` / `PassiveOverlayWindow` / `InteractiveOverlayWindow` three-class hierarchy, which was layered (`WS_EX_LAYERED`) and rendered via `UpdateLayeredWindow`. All layered-window GDI plumbing has been removed.

DWM mica backdrop is applied in `OnHandleCreated` via `DwmSetWindowAttribute` (same as dashboard forms). `DrawToBitmap` captures only GDI+ content — rendered PNGs show the standard WinForms background color, not mica.

## Bitmap Cache

`OverlayPanel._cache` is a `Dictionary<(string style, string status), Bitmap>` — one entry per unique (style, status) combination. Aging tier is NOT baked into the cached glyph. Cache is populated lazily; cleared by `InvalidateStyleCache()` (called when the user changes icon styles). Dispose loop runs over `.Values` in both `InvalidateStyleCache` and `Dispose`.

**Cache miss path**: built-in shape via `GetShapeDelegate` OR pack icon via `RenderFromPack`. Fallback on exception: circle via `RenderCircleFallback`.

## Aging Tier and OnPaint Opacity Ladder

AgingTier 0-4 is computed by `StatusMap.GetAgingTier` in `Imrdy.Core/Status/StatusMap.cs`:

| Tier | Time since last seen |
|------|---------------------|
| 0    | < 1 min             |
| 1    | < 3 min             |
| 2    | < 7 min             |
| 3    | < 15 min            |
| 4    | 15 min+             |

**Aging is NOT baked into the cached glyph.** `OverlayPanel._cache` stores glyphs at full brightness, keyed by `(style, status)` only. Aging is applied at paint time as a chip-background opacity ladder in `OnPaint`: tier 0 is most opaque, tier 4 is faintest. Tier 4 also applies a slight additional glyph dim. No `ColorMatrix` is used in the overlay rendering path.

Note: the tray-icon renderers (`ParametricShapeRenderer`, `PackIconRenderer` in `src/Imrdy.Windows/Icons/`) still bake tier-based aging into their per-icon bitmaps (RGB multiplier for built-in shapes; `ApplyAgingColorMatrix` for SVG pack icons). That path is unchanged and separate from the overlay.

## OnPaint Rendering Flow

`OverlayPanel.OnPaint` (or the drain-tick update method that triggers `Invalidate`):
1. Gets or creates bitmaps for each display item from the lazy cache.
2. Composites items into a horizontal row via `Graphics.DrawImage`.
3. Standard WinForms invalidation / `Refresh` triggers the next `OnPaint`.

No GDI DC juggling, no `UpdateLayeredWindow`, no premultiplied alpha requirement. The non-layered form renders via the normal WinForms paint pipeline.

## Form.Bounds Reliability

On non-layered forms, `Form.Bounds` reflects the actual screen position reliably. WinForms intercepts `WM_WINDOWPOSCHANGED` and updates its internal cache. There is no need for `GetActualWindowRect` or a custom `ActualScreenBounds` property.

**Contrast with the former layered approach**: `OverlayWindowBase.ActualScreenBounds` existed solely because `UpdateLayeredWindow` positions the HWND via Win32 without reliably firing `WM_WINDOWPOSCHANGED` in a way WinForms intercepts — leaving `Form.Bounds` stale at `(0,0,300,300)` for the process lifetime. That staleness bug and its workaround (`GetActualWindowRect` P/Invoke) are gone with the switch to `OnPaint`.

**Implication**: all callers that previously had to use `ActualScreenBounds` (hover dashboard controller bounds checks, z-order gate in `WindowAtPoint`, grace corridor geometry) now use `_overlayPanel.Bounds` directly.

## PInvokeOverlay Surface (Post-Redesign)

Components retained in `src/Imrdy.Windows/Desktop/PInvokeOverlay.cs`:

| Component | Role |
|-----------|------|
| `WS_EX_TOOLWINDOW` | Applied to OverlayPanel's extended window style |
| `ScreenToClientPoint(hwnd, lParam)` | DPI-correct screen→client conversion for hover-highlight poll and hit-testing; 64-bit-safe lParam decode + LOWORD/HIWORD sign-extension built in |
| `WindowAtPoint(point)` | Z-order hit test for hover-dashboard z-order gating |
| `RegisterWindowMessage` | Used for `TaskbarCreated` message ID; OverlayPanel re-pins after Explorer restart |

Components stripped (no longer needed without `WS_EX_LAYERED`):

| Former component | Why removed |
|-----------------|-------------|
| `UpdateLayeredWindow` + GDI P/Invokes | OnPaint replaces the layered bitmap path |
| `SetBitmap` method | Wrapper around `UpdateLayeredWindow`; entire method was layered-only |
| `GetActualWindowRect` + `RECT` struct | Workaround for Bounds-cache staleness; non-layered forms don't have this problem |
| `DecodeLParamPoint` | Merged into `ScreenToClientPoint`; no longer a separate helper |

## Monitor Placement

`OverlayConfig.Monitor` (int) selects which monitor the overlay docks to. `OverlayPanel.CalculatePosition` uses `Screen.AllScreens[config.Monitor]` (clamped to valid range) instead of the former `Screen.PrimaryScreen`-only placement. Dock position (`bottom-right` / `bottom-left`) and offset within the working area remain the same geometry as before.

## Related

- [Overlay Interactivity](overlay-interactivity.md) — OverlayPanel single-class design, input via OnMouseDown/OnMouseUp, ISessionInteractionRouter
- [Status Mapping](status-mapping.md) — StatusMap.GetAgingTier, StatusMap.ResolveColor
- [Render Verb Architecture](render-verb-architecture.md) — overlay component in `imrdy render --all`
