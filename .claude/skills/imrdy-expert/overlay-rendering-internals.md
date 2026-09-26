---
tags: [imrdy-expert/overlay]
summary: "OverlayPanel OnPaint rendering; bitmap cache keyed by (style,status,disconnected); aging via chip-background opacity ladder in OnPaint; the empty-state placeholder chip; Form.Bounds reliability on non-layered forms; TopMost with no watchdog; placement through mutable fields and OverlayPlacement"
last-verified: "2026-09-26"
code-cites:
  - src/Imrdy.Windows/Overlay/OverlayPanel.cs
  - src/Imrdy.Windows/Desktop/PInvokeOverlay.cs
  - src/Imrdy.Core/Status/StatusMap.cs
  - src/Imrdy.Core/Overlay/OverlayPlacement.cs
---

# Overlay Rendering Internals

## Overview

`OverlayPanel` (`src/Imrdy.Windows/Overlay/OverlayPanel.cs`) is a non-layered WinForms `Form` that renders via `OnPaint`. It replaced the former `OverlayWindowBase` / `PassiveOverlayWindow` / `InteractiveOverlayWindow` three-class hierarchy, which was layered (`WS_EX_LAYERED`) and rendered via `UpdateLayeredWindow`.

DWM mica backdrop is applied in `OnHandleCreated` via `ImrdyPalette.ApplyMica` (overlay only — dashboard forms do not use mica; see [Hover Dashboard Form Lifecycle](hover-dashboard-form-lifecycle.md)). `DrawToBitmap` captures only GDI+ content — rendered PNGs show the form background color, not mica.

DWM native corner rounding is applied via `ImrdyPalette.ApplyRoundedCorners(this)` (sets `DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_ROUND`). It returns true on Win11+ (DWM owns the rounding) and false on Win10 ≤19045, which falls back to a GDI `Region` clip via `ApplyRoundedRegion`. `OverlayPanel._usesDwmCorners` tracks which path was taken. The reason for preferring DWM: a GDI `Region` clips only GDI painting, not the DWM mica backdrop, so DWM composited opaque white into Region-carved corners on Win11.

## Bitmap Cache

`OverlayPanel._cache` is a `Dictionary<(string style, string status, bool disconnected), Bitmap>` — one glyph per unique combination, populated lazily by `GetOrCreateBitmap`. The disconnected flag IS part of the key because `DisconnectedGlyph` is a geometry change (shrink to 60% + dashed ring — see [Publisher Liveness](publisher-liveness.md)); the aging tier is NOT, because aging is an opacity treatment applied at paint time. `InvalidateStyleCache()` (called when the user changes icon styles) and `Dispose` both dispose every cached bitmap.

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

Aging is applied in `OnPaint` → `PaintChip` → `ChipBgAlpha(tier, isAlert)` as a chip-background opacity ladder: tier 0 = alpha 255 (most opaque), tier 1 = 200, tier 2 = 160, tier 3 = 120, tier 4 = 80 (faintest). Alert statuses (`permission`/`error`, matched by `IsAlertStatus`) are floored at alpha 160 regardless of tier (Decision 2c). Tier 4 also applies a slight glyph dim (`ColorMatrix.Matrix33 = 0.85f`, only when `tier > 3`); tiers 0-3 use no `ColorMatrix`.

The tray-icon renderers (`ParametricShapeRenderer`, `PackIconRenderer` — see [Tray Icon Rendering](tray-icon-rendering.md)) bake tier-based aging into their per-icon bitmaps instead (RGB multiplier for built-in shapes; `ApplyAgingColorMatrix` for SVG pack icons). That path is separate from the overlay.

## OnPaint Rendering Flow

`OverlayPanel.OnPaint`:
1. `g.Clear(ImrdyPalette.BgForm)`.
2. Empty-state short-circuit: `items.Count == 0` → `PaintPlaceholderChip(g)` and return.
3. Otherwise, left-to-right loop: `chipX = PanelPadding + gripWidth + i * (size + spacing)`; each chip painted via `PaintChip`.

`PaintChip` paints in this fixed order: (1) rounded chip background at tier-driven alpha, (2) status glyph from the cache inset by `ChipPadding`, (3) alert cue outline for error/permission (`PaintAlertCue`), (4) hover highlight when `item.Id == _hoveredChipId` (`PaintHoverHighlight`).

**Paint and hit-test share one slot formula.** `HitIconIndex` subtracts the same `PanelPadding + GripWidth` inset before calling `DisplayItemCollection.TryGetItemAtClientPoint`. `GripWidth` is a single DPI-scaling property over the `GripWidthLogical = 14` seed — paint, hit-test, `IsGripHit` and `MinimumPanelWidth` all read that one value. Any further left-edge element must extend both offsets together, or hit-test and paint will disagree.

### The empty-state placeholder chip

An overlay with no items is never zero-width or invisible — a vanished overlay reads as a crashed tray rather than an idle one (Decision 6). `ApplyItemsAndSize` sizes for `Math.Max(1, items.Count)` slots, and `PaintPlaceholderChip` draws one dimmed imrdy glyph (`placeholderChipAlpha = 50`, `placeholderGlyphAlpha = 0.30f`). So the rendered `empty.png` from `tests/fixtures/overlays/empty.json` (a literal `[]`) shows a single dim chip; that is this path, not a rendering bug. Before filing a rendered-output surprise on an edge-case fixture, grep the paint code for the edge case — a deliberate placeholder path is usually commented with its decision id.

## Form.Bounds Reliability

On non-layered forms, `Form.Bounds` reflects the actual screen position reliably: WinForms intercepts `WM_WINDOWPOSCHANGED` and updates its cache. Callers — hover-dashboard bounds checks, the z-order gate, grace-corridor geometry — use `_overlayPanel.Bounds` directly; there is no `GetActualWindowRect` or `ActualScreenBounds`.

The layered predecessor needed that workaround because `UpdateLayeredWindow` positions the HWND without a `WM_WINDOWPOSCHANGED` WinForms intercepts, leaving `Form.Bounds` stale at `(0,0,300,300)` for the process lifetime. Re-introducing a layered overlay re-introduces that staleness.

## PInvokeOverlay Surface

Components in `src/Imrdy.Windows/Desktop/PInvokeOverlay.cs`:

| Component | Role |
|-----------|------|
| `WS_EX_TOOLWINDOW` | Applied to OverlayPanel's extended window style |
| `ScreenToClientPoint(hwnd, …)` | DPI-correct screen→client conversion for hover-highlight poll and hit-testing (`Bounds` subtraction is wrong above 100% scale) |
| `WindowAtPoint(point)` | Z-order hit test for the hover-dashboard z-order gate |
| `RegisterWindowMessage` | `TaskbarCreated` message ID; OverlayPanel re-pins itself to all virtual desktops after Explorer restart |

The layered-window plumbing (`UpdateLayeredWindow` + GDI P/Invokes, `SetBitmap`, `GetActualWindowRect` + `RECT`, `DecodeLParamPoint`) is gone; `OnPaint` needs none of it.

## TopMost, and no watchdog

`OverlayPanel` sets `Form.TopMost = true` once, in its constructor, and nothing re-asserts
`HWND_TOPMOST` afterwards. Do not add a timer that does. Re-asserting topmost pushes the overlay
above every other topmost window, an open `ContextMenuStrip` included, and clips the menu on every
tick. The earlier layered overlay ran a 5-second `SetWindowPos` watchdog plus a menu-`Opened`
topmost re-apply; commit 01e51c3 deleted both, and the comment that recorded why (removed with the
layered base class in d14e8c0) put it the same way: if the overlay is ever displaced in z-order,
recover at the source of the displacement, not on a periodic timer. Keeping overlay menus open is
already delicate — see [Overlay Context Menus](overlay-context-menus.md).

## Monitor and Position Placement

Placement never reads `_config.*` directly. It reads private mutable fields — `_position`, `_monitor`, `_locked`, `_offsetX`, `_offsetY` — that the ctor initializes from config and that `ApplyPositionConfig(position, monitor, locked, offsetX, offsetY)` overwrites in place, recomputing `Location = CalculatePosition()` without recreating the panel. That is what makes flash-free drag-drop and non-structural config live-reload possible (see [Config Live Reload](config-live-reload.md)). Valid callers of `ApplyPositionConfig` are `OnMouseUp` (drag drop) and `TrayApp.OnConfigChanged` (drain tick) only, asserted with `Debug.Assert(!InvokeRequired, ...)` (stripped in Release). Any further placement input must extend the same field-plus-`ApplyPositionConfig` path.

- `ResolveTargetScreen()` reads `_monitor` against `Screen.AllScreens`, falling back to `Screen.PrimaryScreen ?? screens[0]` when out of range.
- `CalculatePosition()` is a thin wrapper: `OverlayPlacement.ResolveOrigin(_offsetX, _offsetY, _position, screen.WorkingArea, Size)`. The resolution chain (per-monitor offset → `position` anchor → default) and the snap/clamp math live in pure `Imrdy.Core.Overlay.OverlayPlacement`.

**The bottom taskbar reserve is unconditional.** `OverlayPlacement`'s anchor math (behind both `ResolveOrigin`'s anchor branch and `AnchorToOffset`) takes only the working area — no screen bounds — so it cannot tell whether the taskbar is already excluded from `WorkingArea`. It insets left, right and top edges by a 16px `Margin` and the bottom edge by the 8px `BottomTaskbarReserve`, on every Bottom-anchored resolution. On a monitor with a normal (non-auto-hide) taskbar that leaves an extra 8px gap above it; this is an accepted simplification, and widening the signature to take screen bounds is what a conditional reserve would need.

## Related

- [Overlay Interactivity](overlay-interactivity.md) — the overlay's `SurfaceInteracted` / `DragCompleted` event contract
- [Status Mapping](status-mapping.md) — StatusMap.GetAgingTier, StatusMap.ResolveColor
- [Render Verb Architecture](render-verb-architecture.md) — overlay component in `imrdy render --all`
- [Config Live Reload](config-live-reload.md) — structural-delta classification; Position/Monitor/Locked/OffsetX/OffsetY apply in-place via ApplyPositionConfig
