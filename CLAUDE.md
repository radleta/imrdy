# imrdy

Windows system tray monitor for Claude Code sessions. .NET 10, WinForms, single executable.

## Why

Managing multiple Claude Code sessions in parallel is an attention problem: knowing which session needs you, which is working, which is idle, and acting on the right one without losing focus on your work.

imrdy puts that information in the system tray where it stays glanceable in peripheral vision:

- **Dots in the tray** — one icon per active session
- **Color = state** — busy, idle, done (session stopped, teal), needs attention, permission requested, error (tool/stop failures)
- **Aging = dimming** — icons fade as sessions go quiet
- **Click = acknowledge and bring to focus** — switches to the session's virtual desktop and focuses its terminal in one gesture

## Architecture

```
src/Imrdy.Core/          Platform-independent: state files, sound, config, menus, validation
src/Imrdy.Windows/       WinForms tray app, COM desktop interop, CLI commands, hook command
tests/Imrdy.Core.Tests/  Unit tests (xunit + FluentAssertions)
tests/Imrdy.Integration.Tests/  Integration tests (require built binary)
```

### Three Entry Points (Program.cs)

```
imrdy hook        → HookCommand (fast-path, no WinForms, reads stdin JSON, writes state file)
imrdy <command>   → CommandRouter (status|packs|config|workspace|stop, Spectre.Console output)
imrdy             → TrayApp (WinForms ApplicationContext, Application.Run, message pump)
```

The hook runs hundreds of times per session. It uses `HookServiceBuilder` (lightweight DI, no COM/WinForms). The tray uses `MonitorServiceBuilder` (full DI with COM desktop manager).

### Graphics Packs

`ITrayIconRenderer` interface with two impls: `ParametricShapeRenderer` (built-in GDI+ shapes, always-available fallback; replaces `CircleIconRenderer`) and `PackIconRenderer` (SVG via Svg.NET v3.4.7). Six built-in styles: `circles`, `squares`, `triangles`, `diamonds`, `hexagons`, `plus`. `StyleNames` in `Imrdy.Core` provides `NormalizeStyleName` (maps `"dots"` → `"circles"`) and `BuiltInStyles`. Config flag `tray.iconStyle` selects: any built-in name or `"pack:<name>"`. `TrayIconRendererFactory` creates renderers by style name; `TrayApp._rendererCache` (keyed by style) replaces the former single `_renderer` field. Per-session icon style override: tray-owned, persisted via `PersistSessionField`, mirrors SoundPack pattern. Per-workspace icon style override: persisted via `WorkspaceStore.SetIconStyle`, `WorkspaceEntry.IconStyle` field in `workspaces.json`; workspace tray dots render with per-workspace style (global fallback when null). Session icon style resolution chain: session override → workspace override (matched via `Cwd` path) → global `_currentIconStyle`. Sessions with no explicit override inherit their workspace's style. `ResolveSessionIconStyle` in `TrayApp` implements this fallback; changing a workspace's icon style also refreshes all session icons. Packs live at `~/.imrdy/graphics/packs/<name>/` with a `pack.json` manifest. `GraphicsPackLoader` in `Imrdy.Core` mirrors the sound `PackLoader`. Pack load failure silently falls back to circles.

### Overlay (Mode B)

Two concrete classes share an abstract base in `src/Imrdy.Windows/Overlay/`:
- `OverlayWindowBase` — owns rendering, bitmap cache, layered-window plumbing (`WS_EX_LAYERED + WS_EX_TOOLWINDOW`, `TopMost = true`)
- `PassiveOverlayWindow` — adds `WS_EX_TRANSPARENT + WS_EX_NOACTIVATE`; purely visual, no input
- `InteractiveOverlayWindow` — activatable (no `WS_EX_NOACTIVATE`); handles input via `OnMouseDown`/`OnMouseUp` overrides plus `WM_NCHITTEST` for click-through policy

The factory in `TrayApp.CreateOverlay` picks one or the other based on `config.Overlay.Interactive`. No runtime style toggling — interactivity is settled at construction. `PInvokeOverlay.cs` in `src/Imrdy.Windows/Desktop/` holds the layered-window P/Invokes (`UpdateLayeredWindow`, `ScreenToClient`, `DecodeLParamPoint`). No topmost watchdog — `Form.TopMost = true` is sufficient and re-asserting `HWND_TOPMOST` on a timer would clip any open menu.

Renders session characters as a horizontal row at the bottom screen edge via `UpdateLayeredWindow` for per-pixel alpha. Uses `GraphicsPackLoader` directly to render SVGs at overlay size. In built-in mode, renders shapes via `ShapeDefinitions` delegates. Per-item icon style carried via `DisplayItem.IconStyle`. Lazy bitmap cache keyed by `(style, status, tier)` with aging (`ColorMatrix` desaturation). Config: `overlay.enabled`, `overlay.position`, `overlay.size`, `overlay.spacing`, `overlay.interactive`.

**Shared display model**: `DisplayItem` / `DisplayItemCollection` in `Imrdy.Core/Display/` is the unified source of truth for both tray and overlay. `DisplayItemCollection.Build(inputs, trayEnabled)` produces a sorted, filtered list — pure data, no delegates (layer rule: no `System.Windows.Forms` in Core). Both `TrayApp` and the overlay window consume the same `IReadOnlyList<DisplayItem>` snapshot on each drain tick.

**Overlay interactivity**: `OverlayConfig.Interactive` (default `true`; stored as `bool?` so STJ source-gen round-trips correctly — callers use `?? true`). For `InteractiveOverlayWindow`, click-through is a `WM_NCHITTEST` policy: returns `HTCLIENT` over icons (mouse events fire) and `HTTRANSPARENT` over gaps (clicks fall through). For `PassiveOverlayWindow`, `WS_EX_TRANSPARENT` makes the entire window click-through unconditionally. Per Raymond Chen, on a `WS_EX_LAYERED` window the hit-test response governs click-through. `PInvokeOverlay.DecodeLParamPoint` handles 64-bit-safe lParam decoding + LOWORD/HIWORD sign-extension for multi-monitor; `ScreenToClientPoint` P/Invoke handles DPI-correct screen→client conversion (`Bounds`-subtraction is wrong above 100% scale).

**Interaction router**: `ISessionInteractionRouter` (`src/Imrdy.Windows/Interaction/`) is the single entry point for every user-initiated session/workspace interaction, regardless of surface — tray `NotifyIcon.MouseClick`, overlay `OnMouseDown`/`OnMouseUp`, toast activation, controller menu "Switch to X" items. Four methods: `ActivateSession(id)` / `ActivateWorkspace(path)` for primary (left-click) intents and `OpenSessionMenu(id, MenuAnchor)` / `OpenWorkspaceMenu(path, MenuAnchor)` for secondary (right-click) intents. `TrayApp` is the sole implementation; every method follows the same two-phase shape — `MarkSessionInteracted`/`MarkWorkspaceInteracted` resets `LastSeenAt` + refreshes icon, then dispatches the intent. **Call sites MUST NOT call `SwitchToSessionDesktop`, `SwitchToWorkspaceDesktop`, `menu.Show`, or `NotifyIconMenuHost.Show` directly from event handlers** — everything routes through the interface so age-reset and icon-brighten are uniform. Adding a new surface means one call site; adding a new verb means one interface method with one implementation — all surfaces get it for free.

**MenuAnchor**: `MenuAnchor` value type encapsulates the two anchoring modes for right-click menus. `MenuAnchor.AtTrayIcon(NotifyIcon)` dispatches via `NotifyIconMenuHost` (reflection-based private `NotifyIcon.ShowContextMenu`, required because the shell's tray notification context isn't compatible with vanilla `menu.Show`). `MenuAnchor.AtControl(Control, Point)` dispatches via the standard owner-based `ContextMenuStrip.Show(Control, Point)` overload — used by the overlay since its activatable form satisfies the WinForms foreground/hover-hot-track anchor requirement naturally (no `SetForegroundWindow`/`WM_NULL`/`ForceTopMost` band-aids). `TrayApp.ShowContextMenuAt` is the single routing function; `NotifyIconMenuHost` and `menu.Show` are not referenced outside it.

**Overlay context menus**: `InteractiveOverlayWindow.OnMouseUp` (vanilla WinForms event override — NOT `WM_RBUTTONUP` interception) calls `router.OpenSessionMenu/OpenWorkspaceMenu(id, MenuAnchor.AtControl(this, e.Location))`. WinForms then handles foreground transfer, hover hot-tracking, dismissal, and `ToolStripManager.ModalMenuFilter` integration internally.

**Tray god toggle**: `TrayConfig.Enabled` (default `true`). `TrayApp` caches `_trayEnabled` at ctor and updates it in `OnConfigChanged`; `ApplyTrayEnabledToAll` shows/hides tray icons without affecting `OverlayWindow`. Re-enable predicate: `shouldShow = !Dismissed && (RemoveAfter is null || RemoveAfter > now)` — prevents dismissed sessions from reappearing.

### Notification Dwell

`NotificationDwellState` in `Imrdy.Core/Sound/` gates toast and sound notifications behind per-status dwell timers. Icon updates remain immediate; notifications only fire after a session's status has "settled" for its dwell duration (2-5s depending on status). Per-session 10s toast cooldown provides additional backstop. Dwell check piggybacks on the existing 100ms drain timer — no new timer object. `CooldownTracker` (5s per-session sound cooldown) remains as defense-in-depth. `FiredNotification` record carries `PreviousStatus` and `NotificationType` for correct dispatch.

**Teammate-aware gating**: Hook events with `agent_id` (teammate/subagent activity) normally skip lead status updates — they only set `last_teammate_at` on the state file. Exception: when the lead status is "permission" and the teammate fires a permission-resolution event (PostToolUse, PostToolUseFailure, PermissionDenied), the permission is cleared to the derived status. `TeammateGate` in `Imrdy.Core/Hooks/` encapsulates this logic. Sessions with recent teammate activity (within 2 min) suppress `done→idle` dwell entry; instead, consensus promotion in the drain timer checks: when lead is `done` and no teammate activity for 15s (`TeammateQuietThreshold`), promotes to `idle` (green) + toast/sound. `idle_prompt` Notification (60s backstop) is also suppressed when teammates are active — keeps session at "done" (teal) and lets consensus handle promotion. Two speeds to green for teams: consensus (~15s) or wait for teammate presence to age out (2 min). Solo sessions: 5s dwell or 60s `idle_prompt` backstop. `ConsensusPromoted` flag on `SessionEntry` prevents duplicate promotions per done cycle. Sweep timer uses `LastProcessedTimestamp` on `SessionEntry` to skip re-processing unchanged state files.

## Build & Test

```bash
dotnet build                                    # Debug build
dotnet test --filter "Category!=Integration&Category!=Benchmark"  # Unit tests only (421 tests)
./build-dev.sh                                  # Publish → stop tray → deploy to ~/.local/bin/ → auto-respawn
```

Target: `net10.0-windows10.0.17763.0` | PublishSingleFile + SelfContained | No IL trimming (WinForms incompatible)

## Key Conventions

- **Nullable=enable, ImplicitUsings=enable, TreatWarningsAsErrors=true** (Directory.Build.props)
- **File-scoped namespaces** enforced as error
- **_camelCase** private fields, **PascalCase** public members
- **4-space indents** for code, 2-space for XML/JSON/YAML
- CLI commands: static classes with `Run(ServiceProvider, ...)`, use `IAnsiConsole` for output
- All paths centralized in `ImrdyPaths` (config, sessions, logs under `~/.imrdy/`)
- Atomic file writes via `AtomicFileWriter` for config changes
- Source-generated JSON: `ImrdyJsonContext` (no reflection)

## Critical Constraints

**COM Virtual Desktop Interop**: Uses undocumented `IVirtualDesktopManagerInternal` with build-keyed GUIDs (`VirtualDesktopGuids.cs`). Gracefully degrades on unknown Windows builds. Recovers from Explorer restart via lazy re-init on COMException.

**Single Instance**: Mutex-gated via `MutexAcl.TryOpenExisting` (`Global\ImrdyMonitor`). Hook fast-path probes mutex to decide whether to spawn tray.

**Toast Notifications**: Uses `Microsoft.Toolkit.Uwp.Notifications` (WinRT toast API). Click activation fires on background thread — must marshal to UI via `BeginInvoke`. Extracts icon to `~/.imrdy/imrdy.png` for toast logo.

**Stop Signal**: Named `EventWaitHandle` (`Local\ImrdyStop`). `imrdy stop` signals it; tray listens on background thread, posts `ExitThread` to UI thread.

**Hook Logging**: `~/.imrdy/logs/hook_.log` with same rotation as monitor log (1MB, 5 retained files). Info-level: one line per hook event (`SessionId → Status (HookEvent)`). Debug-level raw payloads via `IMRDY_LOG=1`. Uses `shared: true` for concurrent hook process writes.

## Git Workflow

- **main**: releases, PR target
- **develop**: active development
- Tags: `v*` for binary releases, `pack-*` for sound pack releases
