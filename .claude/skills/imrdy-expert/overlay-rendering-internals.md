---
tags: [imrdy-expert/overlay]
summary: "Overlay bitmap cache keyed by (style,status,tier); aging ColorMatrix formula; ActualScreenBounds/GetActualWindowRect necessity on WS_EX_LAYERED+WS_EX_TOOLWINDOW forms; SetBitmap DC ownership rules"
code-cites:
  - src/Imrdy.Windows/Overlay/OverlayWindowBase.cs
  - src/Imrdy.Windows/Desktop/PInvokeOverlay.cs
  - src/Imrdy.Core/Status/StatusMap.cs
---

# Overlay Rendering Internals

## Bitmap Cache

`OverlayWindowBase._cache` is a `Dictionary<(string style, string status, int tier), Bitmap>` — one entry per unique (style, status, aging tier) combination. Cache is populated lazily in `GetOrCreateBitmap`; cleared by `InvalidateStyleCache()` (called when the user changes icon styles). Dispose loop runs over `.Values` in both `InvalidateStyleCache` and `Dispose`.

File: `src/Imrdy.Windows/Overlay/OverlayWindowBase.cs` lines 33, 139-143, 324-331.

**Cache miss path**: `RenderBitmap` -> built-in shape via `GetShapeDelegate` OR pack via `RenderFromPack`. Fallback on exception: circle via `RenderCircleFallback`.

## Aging Tier and ColorMatrix

AgingTier 0-4 is computed by `StatusMap.GetAgingTier` in `Imrdy.Core/Status/StatusMap.cs`:

| Tier | Time since last seen | Brightness |
|------|---------------------|------------|
| 0    | < 1 min             | 1.00       |
| 1    | < 3 min             | 0.85       |
| 2    | < 7 min             | 0.70       |
| 3    | < 15 min            | 0.55       |
| 4    | 15 min+             | 0.40       |

For **built-in shapes** (circles, squares, etc.): aging is applied directly to the RGB bytes before drawing. No ColorMatrix involved. `StatusMap.GetAgingFactorFromTier` returns the brightness factor; `StatusMap.ResolveColor` returns the base (r,g,b).

For **SVG pack icons**: `ApplyAgingColorMatrix` applies a GDI+ ColorMatrix to the pre-rendered SVG bitmap (OverlayWindowBase.cs lines 300-322):

```csharp
var agingScale = 1.0f - (tier / 4.0f);        // tier 0 -> 1.0,  tier 4 -> 0.0
var grayOffset = (1.0f - agingScale) * 0.5f;  // tier 0 -> 0.0,  tier 4 -> 0.5
var alphaMul   = 1.0f - (tier * 0.1f);        // tier 0 -> 1.0,  tier 4 -> 0.6

// 5x5 ColorMatrix rows: [R, G, B, A, W]
// [agingScale, 0, 0, 0, 0]   <- R multiplied
// [0, agingScale, 0, 0, 0]   <- G multiplied
// [0, 0, agingScale, 0, 0]   <- B multiplied
// [0, 0, 0, alphaMul, 0]     <- A multiplied (fade)
// [grayOffset, grayOffset, grayOffset, 0, 1]  <- additive gray translation
```

At tier 4: agingScale=0, grayOffset=0.5, alphaMul=0.6 -> icon becomes a 60%-opaque mid-gray shape.

## UpdateLayeredWindow / SetBitmap Flow

`UpdateItems` (base class, not overridden by subclasses):
1. Composites all items into a single `Format32bppPArgb` bitmap.
2. Calls `PInvokeOverlay.SetBitmap(Handle, composite, position)`.
3. Calls `SetBounds(position.X, position.Y, totalWidth, _config.Size)` as defense-in-depth — documented in source as NOT actually refreshing WinForms' Bounds cache for WS_EX_LAYERED+WS_EX_TOOLWINDOW forms.

`PInvokeOverlay.SetBitmap` (`src/Imrdy.Windows/Desktop/PInvokeOverlay.cs` lines 158-193) calls `UpdateLayeredWindow` with `ULW_ALPHA` and `AC_SRC_ALPHA`. DC ownership rules that must be preserved:

| DC / Handle | Acquire | Release |
|-------------|---------|---------|
| Screen DC (`Graphics.FromHwnd(IntPtr.Zero)`) | `GetHdc()` | `screenGraphics.ReleaseHdc()` NOT DeleteDC |
| Memory DC (`CreateCompatibleDC`) | P/Invoke | `DeleteDC` |
| Bitmap handle (`bitmap.GetHbitmap(Color.FromArgb(0))`) | `GetHbitmap` | `SelectObject(memDc, oldBitmap)` first, then `DeleteObject` |

`GetHbitmap(Color.FromArgb(0))` produces premultiplied alpha as required by `UpdateLayeredWindow` with `ULW_ALPHA`.

## ActualScreenBounds / GetActualWindowRect Necessity

`OverlayWindowBase.ActualScreenBounds` (`OverlayWindowBase.cs` line 80-81) calls `PInvokeOverlay.GetActualWindowRect(Handle)` — which wraps Win32 `GetWindowRect` — instead of reading `Form.Bounds`.

**Why**: On WS_EX_LAYERED + WS_EX_TOOLWINDOW forms, `UpdateLayeredWindow` positions the HWND directly via Win32 without reliably firing `WM_WINDOWPOSCHANGED` in a way WinForms intercepts. Result: WinForms' internal `Bounds` cache stays at the default `(0,0,300,300)` for the entire process lifetime. `SetBounds()` after `SetBitmap` also fails to refresh the cache in this configuration (documented in source comment).

**Consequence**: any code that reads the overlay's screen position — hover dashboard controller bounds checks, z-order gate in `WindowAtPoint`, grace corridor geometry — MUST use `ActualScreenBounds`, not `Form.Bounds` or `Form.Location`. Returns `Rectangle.Empty` before handle is created; callers must guard on `IsHandleCreated`.

## WS_EX_LAYERED-Dependent Surface Area

Components that exist specifically to support `WS_EX_LAYERED` rendering:

| Component | File: line(s) | Role |
|-----------|---------------|------|
| `PInvokeOverlay.SetBitmap` | `Desktop/PInvokeOverlay.cs:158` | Wraps `UpdateLayeredWindow`; entire method is layered-only |
| `PInvokeOverlay.GetActualWindowRect` | `Desktop/PInvokeOverlay.cs:40` | Workaround for Bounds-cache staleness caused by WS_EX_LAYERED |
| `PInvokeOverlay.GetWindowRect` + `RECT` struct | `Desktop/PInvokeOverlay.cs:19-32` | Support for GetActualWindowRect |
| `OverlayWindowBase.ActualScreenBounds` | `Overlay/OverlayWindowBase.cs:80` | Delegates to GetActualWindowRect |
| `OverlayWindowBase._cache` + lifecycle | `Overlay/OverlayWindowBase.cs:33,139,324` | Bitmap pre-render cache; OnPaint path would not use this |
| `GetOrCreateBitmap`, `RenderBitmap`, `ApplyAgingColorMatrix` | `Overlay/OverlayWindowBase.cs:145-322` | Bitmap factory pipeline; non-layered path renders in OnPaint per tick |
| GDI P/Invokes in PInvokeOverlay | `Desktop/PInvokeOverlay.cs:62-100` | `CreateCompatibleDC`, `SelectObject`, `DeleteDC`, `DeleteObject`, `UpdateLayeredWindow`, `ScreenToClient` |
| `SetBounds` defense call in `UpdateItems` | `Overlay/OverlayWindowBase.cs:118` | No-op compensating for Bounds staleness |

**Components that survive a switch to non-layered** (independent of WS_EX_LAYERED):
- `WM_NCHITTEST` override in `InteractiveOverlayWindow` (gap click-through — needed regardless of window style)
- `PInvokeOverlay.DecodeLParamPoint` and `ScreenToClientPoint` (WM_NCHITTEST screen->client conversion)
- `PInvokeOverlay.WindowAtPoint` (z-order hit test — style-independent)
- `WS_EX_NOACTIVATE`, `WS_EX_TOOLWINDOW`, `WS_EX_TRANSPARENT` constants (valid on non-layered windows)
- `IsDashboardHoverActive` flag and `TryGetSessionIdAtScreenPoint` (input routing — style-independent)

## CalculatePosition (Bottom-of-Screen Placement)

`OverlayWindowBase.CalculatePosition` (`OverlayWindowBase.cs:272-279`) uses `Screen.PrimaryScreen.WorkingArea`. Bottom-right (default): `x = wa.Right - contentWidth - 16`. Bottom-left: `x = wa.Left + 16`. `y = wa.Bottom - Size - 16` in both cases. Note: uses `Screen.PrimaryScreen` — does not track the overlay to secondary monitors.
