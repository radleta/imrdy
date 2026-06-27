---
tags: [imrdy-expert/overlay]
summary: "OverlayPanel: single non-layered class replacing the former Passive/Interactive/Base split; context menus dispatched via ISessionInteractionRouter + MenuAnchor.AtControl(owner, location) — vanilla WinForms, no P/Invoke band-aids"
---

# Overlay Interactivity Pattern

## Architecture in One Sentence

`OverlayPanel` is a single non-layered WinForms `Form` that is always interactive. All user actions dispatch through `ISessionInteractionRouter` — left-clicks call `ActivateSession`/`ActivateWorkspace`, right-clicks call `OpenSessionMenu`/`OpenWorkspaceMenu` with `MenuAnchor.AtControl(this, e.Location)`, and the router uses the standard WinForms owner-based `ContextMenuStrip.Show(Control, Point)` internally.

## Class Design (Single Class)

The former three-class hierarchy (`OverlayWindowBase` / `PassiveOverlayWindow` / `InteractiveOverlayWindow`) was collapsed and all three deleted. `OverlayPanel` is a single class:

| Attribute | Value |
|---|---|
| Base | WinForms `Form` (non-layered) |
| Extended styles | `WS_EX_TOOLWINDOW` |
| Rendering | `OnPaint` (no `UpdateLayeredWindow`) |
| Input | `OnMouseDown` / `OnMouseUp` overrides |
| Backdrop | DWM mica (via `DwmSetWindowAttribute`, same as dashboard forms; `DrawToBitmap` captures GDI+ only — no mica in render PNGs) |
| Activatable | yes (always) |
| Click-through | none — inter-chip gaps are opaque panel chrome; clicks there are no-ops (WM_NCHITTEST dropped, Decision 11) |

`OverlayConfig.Interactive` was removed. There is no passive (fully click-through) variant — use `overlay.enabled: false` to suppress the overlay entirely. `OverlayPanel` is recreated on config change (old panel disposed, new panel constructed from fresh config values). `TrayApp.CreateOverlay` now constructs `OverlayPanel` unconditionally with no mode selection.

## Why Always Activatable

`OverlayPanel` must be activatable because right-clicks need to transfer foreground to it so the popup `ContextMenuStrip` receives hover hot-track messages. With `WS_EX_NOACTIVATE`, `SetForegroundWindow` is silently rejected; the menu shows but items don't highlight on hover until the first click "wakes" it (the classic Raymond Chen NotifyIcon-from-non-foreground bug).

The trade-off — clicks momentarily make the overlay foreground — is desirable, not a regression. Every interaction with the overlay either explicitly switches focus (left-click switches to a session terminal) or shows a menu.

## Vanilla Right-Click — No P/Invoke

Overlay right-clicks go through the shared interaction router with an `AtControl` anchor; the router resolves the menu and calls the standard owner-based `Show` overload:

```csharp
// OverlayPanel
protected override void OnMouseUp(MouseEventArgs e)
{
    if (e.Button == MouseButtons.Right && HitIconIndex(e.X, out var idx) && idx < _items.Count)
    {
        var item = _items[idx];
        var anchor = MenuAnchor.AtControl(this, e.Location);
        if (item.ItemType == DisplayItemType.Session)
            _router.OpenSessionMenu(item.Id, anchor);
        else
            _router.OpenWorkspaceMenu(item.Id, anchor);
    }
    base.OnMouseUp(e);
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

`src/Imrdy.Windows/Theme/ImrdyPalette.cs` provides palette colors and `ApplyMica` / `ApplyRoundedRegion` helpers. Extracted from `HoverDashboardFormBase`; consumed by `HoverDashboardFormBase`, `SessionDashboardForm`, `WorkspaceDashboardForm`, and `OverlayPanel`. Use `ImrdyPalette` constants instead of inline `Color.FromArgb` calls in any surface that inherits or hosts overlay-adjacent UI.

## NotifyIconMenuHost — Tray Right-Click Only

`NotifyIconMenuHost` at `src/Imrdy.Windows/Menus/` is **still used for tray-icon right-click** (`NotifyIcon.MouseClick` handler). It reflects `NotifyIcon.ShowContextMenu` (private) and wraps it in `AttachThreadInput`. The tray uses this path because clicks on the `NotifyIcon` arrive via the shell's tray notification protocol, not as `OnMouseUp` events on a form — there's no activatable owner control to pass to `menu.Show`.

The two dispatch modes are unified behind `MenuAnchor`: `MenuAnchor.AtTrayIcon(NotifyIcon)` → `NotifyIconMenuHost`, `MenuAnchor.AtControl(Control, Point)` → `menu.Show(owner, location)`. `TrayApp.ShowContextMenuAt` is the **one and only** site that branches on anchor kind.

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

## Updates — 2026-04-21 (interaction router)

All user-initiated session/workspace actions now route through `ISessionInteractionRouter` contract (`src/Imrdy.Windows/Interaction/`). Four methods: `ActivateSession(id)` / `ActivateWorkspace(path)` for primary intents, `OpenSessionMenu(id, MenuAnchor)` / `OpenWorkspaceMenu(path, MenuAnchor)` for secondary intents. Two-phase shape enforced: `MarkSessionInteracted`/`MarkWorkspaceInteracted` then dispatch. Still applies to `OverlayPanel`.

## Related

- [Architecture](architecture.md) — Overlay rendering loop, timer interactions
- [Status Mapping](status-mapping.md) — Icon color/aging by status
- [Render Verb Architecture](render-verb-architecture.md) — `overlay` component coverage in `imrdy render --all`
