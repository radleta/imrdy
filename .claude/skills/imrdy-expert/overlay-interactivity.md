---
tags: [imrdy-expert/overlay]
summary: "OverlayPanel: single non-layered draggable class (6-anchor snap); context menus dispatched via ISessionInteractionRouter + MenuAnchor.AtControl(owner, location); gutter right-click opens overlay settings via OpenOverlayMenu; WM_MOUSEACTIVATE→MA_NOACTIVATE preserves terminal focus"
---

# Overlay Interactivity Pattern

## Architecture in One Sentence

`OverlayPanel` is a single non-layered WinForms `Form` that is always interactive and draggable. All user actions dispatch through `ISessionInteractionRouter` — left-clicks (non-drag) call `ActivateSession`/`ActivateWorkspace`; chip right-clicks call `OpenSessionMenu`/`OpenWorkspaceMenu` with `MenuAnchor.AtControl(this, e.Location)`; gutter right-clicks (no chip hit) call `OpenOverlayMenu(MenuAnchor.AtControl(this, e.Location))`; drag (threshold-gated from `OnMouseDown`) repositions the panel to one of six snap anchors (top/bottom × left/center/right) with flash-free Position+Monitor persistence. The router uses the standard WinForms owner-based `ContextMenuStrip.Show(Control, Point)` internally.

## Class Design (Single Class)

The former three-class hierarchy (`OverlayWindowBase` / `PassiveOverlayWindow` / `InteractiveOverlayWindow`) was collapsed and all three deleted. `OverlayPanel` is a single class:

| Attribute | Value |
|---|---|
| Base | WinForms `Form` (non-layered) |
| Extended styles | `WS_EX_TOOLWINDOW` |
| Rendering | `OnPaint` (no `UpdateLayeredWindow`) |
| Input | `OnMouseDown` / `OnMouseUp` overrides |
| Backdrop | DWM mica (via `DwmSetWindowAttribute`; overlay only — dashboard forms do not use mica; `DrawToBitmap` captures GDI+ only — no mica in render PNGs) |
| Corner rounding | DWM native `DWMWCP_ROUND` via `ImrdyPalette.ApplyRoundedCorners` (Win11+); GDI `Region` clip fallback for Win10 ≤19045. `_usesDwmCorners` field tracks which path. GDI `Region` alone clips only GDI painting — DWM composited white into region-carved corners on Win11. |
| Activatable | yes (always) |
| Click-through | none — inter-chip gaps are opaque panel chrome; clicks there are no-ops (WM_NCHITTEST dropped, Decision 11) |

`OverlayConfig.Interactive` was removed. There is no passive (fully click-through) variant — use `overlay.enabled: false` to suppress the overlay entirely. `OverlayPanel` is recreated on structural config changes (Enabled/Size/Spacing); non-structural changes (Position/Monitor/Locked) apply in-place via `ApplyPositionConfig` (no flash, no dispose+recreate). `TrayApp.CreateOverlay` constructs `OverlayPanel` unconditionally with no mode selection. A drag-in-flight guard (`_overlayReloadDeferred`) defers any config reload until dragging ends.

## Focus Preservation vs. Activatability

`OverlayPanel` handles `WM_MOUSEACTIVATE` with an unconditional `MA_NOACTIVATE` return (Raymond Chen pattern — no `base.WndProc` call). The overlay **never steals foreground** through any mouse interaction: not on left-click, not on drag initiation, not ever. Terminal focus is preserved throughout.

The form is **not** `WS_EX_NOACTIVATE`, so it can still be activated programmatically (via `SetForegroundWindow`). WinForms' owner-based `menu.Show(Control, Point)` calls `SetForegroundWindow` on the owner internally when opening a `ContextMenuStrip`, bypassing `WM_MOUSEACTIVATE`. Context menus therefore receive hover hot-track messages correctly — no `AttachThreadInput` or foreground-transfer band-aids needed.

Do NOT add `WS_EX_NOACTIVATE` to the overlay extended styles — that would cause `SetForegroundWindow` to be silently rejected and break context-menu hover highlighting.

## Vanilla Right-Click — No P/Invoke

Overlay right-clicks go through the shared interaction router with an `AtControl` anchor; the router resolves the menu and calls the standard owner-based `Show` overload. Right-click dispatches by hit result: chip hit → session/workspace menu; gutter (no chip hit) → overlay settings menu.

```csharp
// OverlayPanel — right-click branch of OnMouseUp
else if (e.Button == MouseButtons.Right)
{
    var anchor = MenuAnchor.AtControl(this, e.Location);
    if (HitIconIndex(e.X, out var idx) && idx < _items.Count)
    {
        // Chip right-click: open the session/workspace context menu.
        var item = _items[idx];
        if (item.ItemType == DisplayItemType.Session)
            _router.OpenSessionMenu(item.Id, anchor);
        else
            _router.OpenWorkspaceMenu(item.Id, anchor);
    }
    else
    {
        // Gutter/padding right-click: open the overlay settings menu.
        _router.OpenOverlayMenu(anchor);
    }
}

// TrayApp (ISessionInteractionRouter impl)
public void OpenSessionMenu(string id, MenuAnchor anchor)
{
    MarkSessionInteracted(id);                    // age-reset + icon refresh
    var menu = LookupSessionMenu(id);
    if (menu != null) ShowContextMenuAt(menu, anchor);  // single branch point for AtControl vs AtTrayIcon
}
```

`ShowContextMenuAt` is the single place that branches on anchor kind (`AtControl` → `menu.Show(owner, location)`; `AtTrayIcon` → `NotifyIconMenuHost`). WinForms' internal `ToolStripManager.ModalMenuFilter` handles foreground/dismissal because the owner is a real activatable form.

## Drag-to-Reposition

`OverlayPanel` supports drag repositioning with 6-anchor edge snap (top/bottom × left/center/right). The drag FSM runs across three mouse event overrides:

- `OnMouseDown` (left button): records `_dragStartScreen`, `_downHitIndex`, `_formStartLocation`; sets `_dragArmed = true`; calls `Capture = true` (tracks cursor outside panel bounds).
- `OnMouseMove`: if `_isDragging`, translates `(dx, dy)` to logical pixels via `DeviceDpi / 96f` scale and moves `this.Location`; if `_dragArmed` and threshold (`SystemInformation.DragSize`) exceeded, promotes to `_isDragging = true`; sets `Cursor.Current = Cursors.SizeAll` during capture.
- `OnMouseUp` (left button): if `_isDragging` → `ComputeSnap()` → `ApplyPositionConfig(position, monitor, _locked)` → async `ConfigReader.Update`; if not dragging (click) → dispatch to `ActivateSession`/`ActivateWorkspace`.

`OnKeyDown` (Escape while dragging): resets drag state and reverts `this.Location` to the persisted anchor via `CalculatePosition()`. `WM_CANCELMODE` (foreground stolen by alt-tab): calls `ResetDragState()` — cursor capture released, position reverted on next `CalculatePosition` call (UpdateItems tick).

`overlay.locked: true` disables drag initiation — `OnMouseDown` still records `_downHitIndex` for click dispatch but does not set `_dragArmed`. Hover cursor in gutter becomes `Cursors.Default` (not `SizeAll`) when locked.

`ComputeSnap()` returns `(position, monitor)` — the 6-anchor string nearest the current panel center and the index of the screen it landed on. `ApplyPositionConfig` then calls `CalculatePosition` (which calls `OverlayAnchor.Parse(position)`) and sets `this.Location` in-place — same path as the non-structural config fast path.

## Hit-Testing (No WM_NCHITTEST Override)

The `WM_NCHITTEST` gap click-through policy was dropped in the OverlayPanel redesign (Decision 11). `OverlayPanel` has **no `WM_NCHITTEST` override** in `WndProc`.

**Rationale**: the former layered overlay had a large mostly-transparent bounding box, so gaps between icons needed `HTTRANSPARENT` to let clicks fall through. The new bounded mica panel has no transparent regions — inter-chip gaps are opaque panel chrome. A click in a gap is a simple no-op: `HitIconIndex` returns false, the `OnMouseDown`/`OnMouseUp` handlers take no action.

`PInvokeOverlay.ScreenToClientPoint(hwnd, lParam)` is still used for DPI-correct screen→client conversion in the hover-highlight poll and hit-testing. **Do NOT** substitute `Bounds.Left`/`Bounds.Top` subtraction — it diverges from the OS transform at DPI scales > 100%. The helper wraps the Win32 `ScreenToClient` P/Invoke with 64-bit-safe lParam decoding and LOWORD/HIWORD sign-extension for multi-monitor setups.

Note: `DecodeLParamPoint` (the former standalone helper) was removed from `PInvokeOverlay`. Decoding is now done inside `ScreenToClientPoint`.

## PInvokeOverlay Surface

`PInvokeOverlay.cs` in `src/Imrdy.Windows/Desktop/` retains:
- `WS_EX_TOOLWINDOW` constant
- `ScreenToClientPoint(hwnd, lParam)` — DPI-correct screen→client conversion
- `WindowAtPoint(point)` — used by hover-dashboard z-order gating

Stripped (no longer needed without `WS_EX_LAYERED`):
- `UpdateLayeredWindow` and associated GDI plumbing
- `DecodeLParamPoint` (merged into `ScreenToClientPoint`)
- `WS_EX_LAYERED` constant

Added:
- `RegisterWindowMessage` — used for `TaskbarCreated` message registration so the overlay can re-pin after Explorer restart.

## SurfaceInteracted Event

`OverlayPanel` exposes `SurfaceInteracted` (fires after a successful left-click dispatch, inside the try block, before any catch). Right-click does NOT fire the event — WinForms handles menu dismissal naturally.

**Subscription lifecycle (P6 — TrayApp owns all wiring):**
- TrayApp subscribes to `_overlayPanel.SurfaceInteracted` after construction
- On config change: capture old reference, unsubscribe, dispose old panel, construct new panel, re-subscribe
- Controllers call `HandleSurfaceInteraction()` via TrayApp dispatch — base ctor does NOT self-subscribe

## ImrdyPalette — Shared Theme

`src/Imrdy.Windows/Theme/ImrdyPalette.cs` provides palette colors and `ApplyMica` / `ApplyRoundedRegion` / `ApplyRoundedCorners` helpers. Extracted from `HoverDashboardFormBase`; consumed by `HoverDashboardFormBase`, `SessionDashboardForm`, `WorkspaceDashboardForm`, and `OverlayPanel`. `ApplyRoundedCorners(Form): bool` sets `DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_ROUND`; returns true when DWM takes ownership of rounding (Win11+), false when the caller must fall back to `ApplyRoundedRegion` (Win10). Use `ImrdyPalette` constants instead of inline `Color.FromArgb` calls in any surface that inherits or hosts overlay-adjacent UI.

## NotifyIconMenuHost — Tray Right-Click Only

`NotifyIconMenuHost` at `src/Imrdy.Windows/Menus/` is **still used for tray-icon right-click** (`NotifyIcon.MouseClick` handler). It reflects `NotifyIcon.ShowContextMenu` (private) and wraps it in `AttachThreadInput`. The tray uses this path because clicks on the `NotifyIcon` arrive via the shell's tray notification protocol, not as `OnMouseUp` events on a form — there's no activatable owner control to pass to `menu.Show`.

The two dispatch modes are unified behind `MenuAnchor`: `MenuAnchor.AtTrayIcon(NotifyIcon)` → `NotifyIconMenuHost`, `MenuAnchor.AtControl(Control, Point)` → `menu.Show(owner, location)`. `TrayApp.ShowContextMenuAt` is the **one and only** site that branches on anchor kind.

## Updates — 2026-06-27 (DWM corner rounding; mica removed from dashboards; placement fix)

- **DWM native corner rounding on overlay** — `ImrdyPalette.ApplyRoundedCorners(Form): bool` added; `OverlayPanel` calls it first and falls back to `ApplyRoundedRegion` only when it returns false (Win10 ≤19045). Root cause: GDI `Region` clipped only GDI painting, not the DWM mica backdrop, producing opaque white corners on Win11.
- **Mica removed from dashboards** — `ImrdyPalette.ApplyMica` is no longer called from `HoverDashboardFormBase.OnHandleCreated`. Dashboards fade via `Form.Opacity` (layered window); mica on a layered form composites white into GDI-Region-clipped corners.
- **Dashboard anchor X constrained to overlay span** — `ComputeAnchorPlacement` now biases anchorX toward the hovered chip within the overlay's horizontal span (falls back to overlay center; final working-area clamp retained). Cursor-centering pinned the popup to the screen edge for an edge-docked overlay.

## Updates — 2026-06-26 (OverlayPanel redesign)

Supersedes the 2026-04-19 Passive/Interactive split and the 2026-04-21 interaction-router additions:

- **Collapsed three-class hierarchy to single `OverlayPanel`** — `OverlayWindowBase`, `PassiveOverlayWindow`, `InteractiveOverlayWindow` all deleted.
- **Non-layered rendering** — replaced `UpdateLayeredWindow` + GDI bitmap path with `OnPaint` + DWM mica backdrop.
- **`OverlayConfig.Interactive` removed** — panel is always interactive. No runtime mode switching. No passive variant.
- **`OverlayConfig.Monitor` added** — int field for multi-monitor selection.
- **Spacing default** — bumped from 4 to 8 px.
- **`DecodeLParamPoint` removed from PInvokeOverlay** — merged into `ScreenToClientPoint`.
- **`RegisterWindowMessage` added** — for `TaskbarCreated` re-pin after Explorer restart.
- **`ImrdyPalette` extracted** — palette colors and DWM helpers consolidated into shared theme class.
- **`IsDashboardHoverActive` removed from hover-dashboard controllers** — z-order gate now reads `Form.Bounds` directly.
- **`overlay` render component added** — `OverlayRenderer` + `NullSessionInteractionRouter`; 4 fixture files; covered by `imrdy render --all`.

## Updates — 2026-07-01 (drag + OpenOverlayMenu + Locked + structural-delta reload)

- **Drag-to-reposition** — `OverlayPanel` now supports threshold-gated drag to one of six snap anchors (top/bottom × left/center/right); `OnMouseMove` added; `OnMouseUp` distinguishes drag completion from click; `ComputeSnap` + `ApplyPositionConfig` for in-place positioning; async `ConfigReader.Update` persists Position+Monitor.
- **`OpenOverlayMenu(MenuAnchor)` — 5th router method** — gutter right-click (no chip hit) calls `_router.OpenOverlayMenu`. `OverlayMenuBuilder` in `src/Imrdy.Windows/Menus/` builds the overlay settings submenu (6 positions, spacing presets, per-monitor selector, Lock toggle). Reachable from both the tray controller menu and the overlay gutter right-click.
- **`OverlayConfig.Locked`** — new bool field (default false). Disables drag when true; hover cursor in gutter becomes `Cursors.Default`. `overlay.locked` is persisted via `ConfigReader.Update`. Lock toggle is in the overlay settings submenu.
- **WM_MOUSEACTIVATE → MA_NOACTIVATE** — `WndProc` now always returns MA_NOACTIVATE (no base call). Terminal focus is preserved through all mouse interactions. "Why Always Activatable" section renamed to "Focus Preservation vs. Activatability".
- **Structural-delta reload** — `OnConfigChanged` gains a non-structural fast path: Position/Monitor/Locked changes call `ApplyPositionConfig` in-place; only Enabled/Size/Spacing trigger dispose+recreate. `_overlayReloadDeferred` defers reload while drag is in flight.

## Updates — 2026-04-21 (interaction router)

All user-initiated session/workspace actions now route through `ISessionInteractionRouter` contract (`src/Imrdy.Windows/Interaction/`). Initially four methods: `ActivateSession(id)` / `ActivateWorkspace(path)` for primary intents, `OpenSessionMenu(id, MenuAnchor)` / `OpenWorkspaceMenu(path, MenuAnchor)` for secondary intents. A fifth method (`OpenOverlayMenu`) was added in 2026-07-01. Two-phase shape enforced: `MarkSessionInteracted`/`MarkWorkspaceInteracted` then dispatch. Still applies to `OverlayPanel`.

## Related

- [Architecture](architecture.md) — Overlay rendering loop, timer interactions
- [Status Mapping](status-mapping.md) — Icon color/aging by status
- [Render Verb Architecture](render-verb-architecture.md) — `overlay` component coverage in `imrdy render --all`
