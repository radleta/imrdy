---
tags: [imrdy/overlay-interaction]
updated: 2026-04-21
summary: "Overlay split into Passive/Interactive classes; context menus dispatched via ISessionInteractionRouter + MenuAnchor.AtControl(owner, location) — vanilla WinForms, no P/Invoke band-aids"
---

# Overlay Interactivity Pattern

## Architecture in One Sentence

The overlay is split into two classes (`PassiveOverlayWindow`, `InteractiveOverlayWindow`) sharing `OverlayWindowBase`. The interactive variant is **activatable** (no `WS_EX_NOACTIVATE`) and dispatches all user actions through `ISessionInteractionRouter` — left-clicks call `ActivateSession`/`ActivateWorkspace`, right-clicks call `OpenSessionMenu`/`OpenWorkspaceMenu` with `MenuAnchor.AtControl(this, e.Location)`, and the router uses the standard WinForms owner-based `ContextMenuStrip.Show(Control, Point)` internally.

## Class Split

| | `PassiveOverlayWindow` | `InteractiveOverlayWindow` |
|---|---|---|
| ExStyles added | `WS_EX_TRANSPARENT` + `WS_EX_NOACTIVATE` | none |
| Input handling | none | `OnMouseDown` / `OnMouseUp` overrides + `WM_NCHITTEST` |
| Activatable | no | yes |
| Use case | purely visual, never receives clicks | clickable, owns context menus |

`OverlayWindowBase` provides `WS_EX_LAYERED + WS_EX_TOOLWINDOW`, `TopMost = true`, the bitmap cache, `UpdateLayeredWindow` rendering, and `HitIconIndex` slot math. `TrayApp.CreateOverlay` picks the concrete class based on `config.Overlay.Interactive` at construction time — no runtime mode switching.

## Why Activatable

The interactive overlay must be activatable because right-clicks need to transfer foreground to it so the popup `ContextMenuStrip` receives hover hot-track messages. With `WS_EX_NOACTIVATE`, `SetForegroundWindow` is silently rejected; the menu shows but items don't highlight on hover until the first click "wakes" it (the classic Raymond Chen NotifyIcon-from-non-foreground bug).

The trade-off — clicks momentarily make the overlay foreground — is desirable, not a regression. Every interaction with the overlay either explicitly switches focus (left-click switches to a session terminal) or shows a menu. There is no "incidental" click on an interactive control that should NOT take focus.

The passive variant keeps `WS_EX_NOACTIVATE` because it never receives input — it's purely a status display.

## Vanilla Right-Click — No P/Invoke

Overlay right-clicks go through the shared interaction router with an `AtControl` anchor; the router resolves the menu and calls the standard owner-based `Show` overload:

```csharp
// InteractiveOverlayWindow
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

That's the entire overlay-menu path. `ShowContextMenuAt` is the single place that branches on anchor kind (`AtControl` → `menu.Show(owner, location)`; `AtTrayIcon` → `NotifyIconMenuHost`). WinForms' internal `ToolStripManager.ModalMenuFilter` handles foreground/dismissal because the owner is a real activatable form.

**What we deleted to get here** (~150 lines across the codebase):
- `SetForegroundWindow` + `PostMessage(WM_NULL)` P/Invoke calls
- `OverlayMenuPresenter` class + hidden owner `NativeWindow`
- `ForceTopMost` / `SetWindowPos` / `HWND_TOPMOST` plumbing
- `ContextMenuStrip.Opened` handler that re-applied topmost
- 5-second `TopMost` watchdog timer
- `ApplyInteractiveStyle` runtime style-toggling
- `WM_RBUTTONUP` / `WM_LBUTTONDOWN` interception in `WndProc` with `BeginInvoke` defer

Every one of those was compensating for a single root cause: cutting WinForms out of its own menu pipeline by intercepting at the message-pump level and using the owner-less `Show(Point)` overload.

## Hit-Test Policy (Click-Through)

Click-through over gaps between icons is the only Win32-level concern that has no managed equivalent:

```csharp
// InteractiveOverlayWindow.WndProc — only intercepts WM_NCHITTEST
if (m.Msg == WM_NCHITTEST)
{
    var (sx, sy) = PInvokeOverlay.DecodeLParamPoint(m.LParam); // SCREEN coords
    PInvokeOverlay.ScreenToClientPoint(Handle, ref sx, ref sy);
    m.Result = HitIconIndex(sx, out _) ? (IntPtr)HTCLIENT : (IntPtr)HTTRANSPARENT;
    return;
}
base.WndProc(ref m);  // ALWAYS call base for everything else
```

**Coordinate-space rule:** `WM_NCHITTEST` lParam is **screen coordinates** — call `ScreenToClientPoint` after `DecodeLParamPoint`. Mouse events handled in `OnMouseDown`/`OnMouseUp` already arrive in client coordinates via `MouseEventArgs.Location`.

`PassiveOverlayWindow` doesn't override `WndProc` at all — `WS_EX_TRANSPARENT` makes the OS skip hit-testing entirely.

## DecodeLParamPoint Helper

`PInvokeOverlay.DecodeLParamPoint(IntPtr)` returns `(int X, int Y)` and handles two correctness concerns:

- **64-bit safety:** `(int)(nint)lParam` — NOT `IntPtr.ToInt32()` which throws `OverflowException` when upper 32 bits are non-zero.
- **Sign-extension:** LOWORD/HIWORD are signed shorts; negative values legitimately occur on multi-monitor setups where the window sits on a monitor positioned left/above the primary. Coords ≥ 0x8000 are subtracted from 0x10000.

`PInvokeOverlay.ScreenToClientPoint(hwnd, ref x, ref y)` wraps the Win32 `ScreenToClient` P/Invoke. **Do NOT** substitute `Bounds.Left`/`Bounds.Top` subtraction — it diverges from the OS transform at DPI scales > 100%.

## OverlayConfig.Interactive Typing

Stored as `bool?` (nullable), not `bool`. STJ source-gen ignores CLR field initializers (`= true`), so a missing `interactive` key in the JSON config deserializes to `null`, not `true`. `EnsureDefaults` coalesces:

```csharp
overlay = overlay with { Interactive = overlay.Interactive ?? true };
```

Callers also coalesce at point-of-use as defense-in-depth: `config.Overlay.Interactive ?? true`.

## NotifyIconMenuHost — Tray Right-Click Only

`NotifyIconMenuHost` at `src/Imrdy.Windows/Menus/` is **still used for tray-icon right-click** (`NotifyIcon.MouseClick` handler). It reflects `NotifyIcon.ShowContextMenu` (private) and wraps it in `AttachThreadInput`. The tray uses this path because clicks on the `NotifyIcon` arrive via the shell's tray notification protocol, not as `OnMouseUp` events on a form — there's no activatable owner control to pass to `menu.Show`.

The two dispatch modes are unified behind `MenuAnchor`: `MenuAnchor.AtTrayIcon(NotifyIcon)` → `NotifyIconMenuHost`, `MenuAnchor.AtControl(Control, Point)` → `menu.Show(owner, location)`. `TrayApp.ShowContextMenuAt` is the **one and only** site that branches on anchor kind; `NotifyIconMenuHost` and `menu.Show` are not referenced anywhere else — not from the overlay, not from toast activation, not from controller-menu items.

## Updates — 2026-04-21 (interaction router)

All user-initiated session/workspace actions — tray click, overlay click, toast activation, controller menu, session menu — now route through a single `ISessionInteractionRouter` contract (`src/Imrdy.Windows/Interaction/`). Four methods: `ActivateSession(id)` / `ActivateWorkspace(path)` for primary (left-click) intents, `OpenSessionMenu(id, MenuAnchor)` / `OpenWorkspaceMenu(path, MenuAnchor)` for secondary (right-click) intents.

- **Overlay right-click now calls `router.OpenSessionMenu/OpenWorkspaceMenu` with `MenuAnchor.AtControl(this, e.Location)`** — replaces the earlier `IOverlayClickRouter.ShowContextMenu(owner, location, id, type)` contract (which has been deleted).
- **`MenuAnchor` value type** unifies the two anchoring modes: `AtTrayIcon(NotifyIcon)` → `NotifyIconMenuHost`; `AtControl(Control, Point)` → `menu.Show(owner, location)`. Single branch point lives in `TrayApp.ShowContextMenuAt`.
- **Two-phase shape enforced by the contract** — every router method internally runs `MarkSessionInteracted`/`MarkWorkspaceInteracted` (age-reset + icon refresh) **then** dispatches the intent. Callers no longer duplicate this bookkeeping inline per surface.
- **Hand cursor on interactive overlay** — `InteractiveOverlayWindow` sets `Cursor = Cursors.Hand` at construction. `WM_NCHITTEST` returning `HTTRANSPARENT` over gaps means the cursor is only visible over icons; over gaps the OS routes cursor selection to the window below.

Root-cause insight: every surface (tray, overlay, toast, controller menu) had been duplicating the age-reset + icon-refresh logic inline at the event handler. The router contract enforces the uniform two-phase shape — Mark, then Dispatch. Adding a new surface is one call site; adding a new verb (e.g. `DismissSession`) is one interface method and one implementation — all surfaces get it for free.

## Updates — 2026-04-19 (vanilla refactor)

Supersedes the earlier "delegate to NotifyIcon" approach. Implemented in the post-Step-12 cleanup of `tray-overlay-parity`:

- **Split overlay into `Passive` / `Interactive` / `Base`** — interactivity decided at construction, not runtime. `WS_EX_NOACTIVATE` moved off the interactive variant so it can be foreground for menus.
- **Vanilla mouse handling** — replaced `WM_LBUTTONDOWN` / `WM_RBUTTONUP` interception with `OnMouseDown` / `OnMouseUp` overrides that always call `base`. `WndProc` now intercepts only `WM_NCHITTEST`.
- **Vanilla menu show** — `IOverlayClickRouter.ShowContextMenu` now takes `(Control owner, Point clientLocation, string id, DisplayItemType type)`; implementation is `menu.Show(owner, clientLocation)`. Menu anchors at the click point.
- **Removed topmost watchdog** — `Form.TopMost = true` is sufficient.
- **Deleted P/Invoke band-aids** — `SetForegroundWindow`, `PostWmNull`, `ForceTopMost`, `SetWindowPos`, `ReapplyTopMost` removed from `PInvokeOverlay`. Class shrunk significantly.
- **Deleted `OverlayMenuPresenter`** + hidden owner `NativeWindow` — the activatable interactive overlay IS the owner.

## Related

- [Architecture](architecture.md) — Overlay rendering loop, timer interactions
- [Status Mapping](status-mapping.md) — Icon color/aging by status
