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

### Seven Entry Points (Program.cs)

```
imrdy hook                          → HookCommand (fast-path, no WinForms, reads stdin JSON, writes state file)
imrdy <command>                     → CommandRouter (status|packs|config|workspace|wsl|stop|inspect-live|render-live, Spectre.Console output)
imrdy preview-dashboard <fixture>   → PreviewDashboardCommand (standalone WinForms dev tool; inline ServiceCollection, bypasses mutex, deserializes DashboardViewModel fixture via ImrdyJsonContext, runs DashboardForm pinned)
imrdy render <component> [args]     → RenderCommand (in-process UI artifact capture; bypasses mutex; placed between preview-dashboard and tray fallback)
imrdy inspect-live <id>             → InspectLiveCommand (CLI client; connects to tray via Local\ImrdyInspect pipe; prints/writes walker+analyzer JSON)
imrdy render-live <id> --output F   → RenderLiveCommand (CLI client; connects to tray via Local\ImrdyInspect pipe; captures live DashboardForm PNG)
imrdy                               → TrayApp (WinForms ApplicationContext, Application.Run, message pump)
```

The hook runs hundreds of times per session. It uses `HookServiceBuilder` (lightweight DI, no COM/WinForms). The tray uses `MonitorServiceBuilder` (full DI with COM desktop manager). The preview-dashboard branch is placed between the Spectre CLI branch and the tray fallback — Spectre skips WinForms init; preview needs it. Mutex check is intentionally bypassed so preview runs alongside the real tray. The `inspect-live` and `render-live` CLI commands are thin clients — they send a request over the named pipe and print/write the response; all heavy work (walking, rendering) runs inside the already-running tray process on the UI thread.

### Graphics Packs

`ITrayIconRenderer` interface with two impls: `ParametricShapeRenderer` (built-in GDI+ shapes, always-available fallback; replaces `CircleIconRenderer`) and `PackIconRenderer` (SVG via Svg.NET v3.4.7). Six built-in styles: `circles`, `squares`, `triangles`, `diamonds`, `hexagons`, `plus`. `StyleNames` in `Imrdy.Core` provides `NormalizeStyleName` (maps `"dots"` → `"circles"`) and `BuiltInStyles`. Config flag `tray.iconStyle` selects: any built-in name or `"pack:<name>"`. `TrayIconRendererFactory` creates renderers by style name; `TrayApp._rendererCache` (keyed by style) replaces the former single `_renderer` field. Per-session icon style override: tray-owned, persisted via `PersistSessionField`, mirrors SoundPack pattern. Per-workspace icon style override: persisted via `WorkspaceStore.SetIconStyle`, `WorkspaceEntry.IconStyle` field in `workspaces.json`; workspace tray dots render with per-workspace style (global fallback when null). Session icon style resolution chain: session override → workspace override (matched via `Cwd` path) → global `_currentIconStyle`. Sessions with no explicit override inherit their workspace's style. `ResolveSessionIconStyle` in `TrayApp` implements this fallback; changing a workspace's icon style also refreshes all session icons. Packs live at `~/.imrdy/graphics/packs/<name>/` with a `pack.json` manifest. `GraphicsPackLoader` in `Imrdy.Core` mirrors the sound `PackLoader`. Pack load failure silently falls back to circles.

### Overlay (Mode B)

Two concrete classes share an abstract base in `src/Imrdy.Windows/Overlay/`:
- `OverlayWindowBase` — owns rendering, bitmap cache, layered-window plumbing (`WS_EX_LAYERED + WS_EX_TOOLWINDOW`, `TopMost = true`)
- `PassiveOverlayWindow` — adds `WS_EX_TRANSPARENT + WS_EX_NOACTIVATE`; purely visual, no input
- `InteractiveOverlayWindow` — activatable (no `WS_EX_NOACTIVATE`); handles input via `OnMouseDown`/`OnMouseUp` overrides plus `WM_NCHITTEST` for click-through policy; exposes `SurfaceInteracted` event, `IsDashboardHoverActive` flag, and `TryGetSessionIdAtScreenPoint` for the hover dashboard controller

The factory in `TrayApp.CreateOverlay` picks one or the other based on `config.Overlay.Interactive`. No runtime style toggling — interactivity is settled at construction. `PInvokeOverlay.cs` in `src/Imrdy.Windows/Desktop/` holds the layered-window P/Invokes (`UpdateLayeredWindow`, `ScreenToClient`, `DecodeLParamPoint`). No topmost watchdog — `Form.TopMost = true` is sufficient and re-asserting `HWND_TOPMOST` on a timer would clip any open menu.

Renders session characters as a horizontal row at the bottom screen edge via `UpdateLayeredWindow` for per-pixel alpha. Uses `GraphicsPackLoader` directly to render SVGs at overlay size. In built-in mode, renders shapes via `ShapeDefinitions` delegates. Per-item icon style carried via `DisplayItem.IconStyle`. Lazy bitmap cache keyed by `(style, status, tier)` with aging (`ColorMatrix` desaturation). Config: `overlay.enabled`, `overlay.position`, `overlay.size`, `overlay.spacing`, `overlay.interactive`.

**Shared display model**: `DisplayItem` / `DisplayItemCollection` in `Imrdy.Core/Display/` is the unified source of truth for both tray and overlay. `DisplayItemCollection.Build(inputs, trayEnabled)` produces a sorted, filtered list — pure data, no delegates (layer rule: no `System.Windows.Forms` in Core). Both `TrayApp` and the overlay window consume the same `IReadOnlyList<DisplayItem>` snapshot on each drain tick.

**Overlay interactivity**: `OverlayConfig.Interactive` (default `true`; stored as `bool?` so STJ source-gen round-trips correctly — callers use `?? true`). For `InteractiveOverlayWindow`, click-through is a `WM_NCHITTEST` policy: returns `HTCLIENT` over icons (mouse events fire) and `HTTRANSPARENT` over gaps (clicks fall through). For `PassiveOverlayWindow`, `WS_EX_TRANSPARENT` makes the entire window click-through unconditionally. Per Raymond Chen, on a `WS_EX_LAYERED` window the hit-test response governs click-through. `PInvokeOverlay.DecodeLParamPoint` handles 64-bit-safe lParam decoding + LOWORD/HIWORD sign-extension for multi-monitor; `ScreenToClientPoint` P/Invoke handles DPI-correct screen→client conversion (`Bounds`-subtraction is wrong above 100% scale).

**Interaction router**: `ISessionInteractionRouter` (`src/Imrdy.Windows/Interaction/`) is the single entry point for every user-initiated session/workspace interaction, regardless of surface — tray `NotifyIcon.MouseClick`, overlay `OnMouseDown`/`OnMouseUp`, toast activation, controller menu "Switch to X" items. Four methods: `ActivateSession(id)` / `ActivateWorkspace(path)` for primary (left-click) intents and `OpenSessionMenu(id, MenuAnchor)` / `OpenWorkspaceMenu(path, MenuAnchor)` for secondary (right-click) intents. `TrayApp` is the sole implementation; every method follows the same two-phase shape — `MarkSessionInteracted`/`MarkWorkspaceInteracted` resets `LastSeenAt` + refreshes icon, then dispatches the intent. **Call sites MUST NOT call `SwitchToSessionDesktop`, `SwitchToWorkspaceDesktop`, `menu.Show`, or `NotifyIconMenuHost.Show` directly from event handlers** — everything routes through the interface so age-reset and icon-brighten are uniform. Adding a new surface means one call site; adding a new verb means one interface method with one implementation — all surfaces get it for free.

**MenuAnchor**: `MenuAnchor` value type encapsulates the two anchoring modes for right-click menus. `MenuAnchor.AtTrayIcon(NotifyIcon)` dispatches via `NotifyIconMenuHost` (reflection-based private `NotifyIcon.ShowContextMenu`, required because the shell's tray notification context isn't compatible with vanilla `menu.Show`). `MenuAnchor.AtControl(Control, Point)` dispatches via the standard owner-based `ContextMenuStrip.Show(Control, Point)` overload — used by the overlay since its activatable form satisfies the WinForms foreground/hover-hot-track anchor requirement naturally (no `SetForegroundWindow`/`WM_NULL`/`ForceTopMost` band-aids). `TrayApp.ShowContextMenuAt` is the single routing function; `NotifyIconMenuHost` and `menu.Show` are not referenced outside it.

**Overlay context menus**: `InteractiveOverlayWindow.OnMouseUp` (vanilla WinForms event override — NOT `WM_RBUTTONUP` interception) calls `router.OpenSessionMenu/OpenWorkspaceMenu(id, MenuAnchor.AtControl(this, e.Location))`. WinForms then handles foreground transfer, hover hot-tracking, dismissal, and `ToolStripManager.ModalMenuFilter` integration internally.

**Tray god toggle**: `TrayConfig.Enabled` (default `true`). `TrayApp` caches `_trayEnabled` at ctor and updates it in `OnConfigChanged`; `ApplyTrayEnabledToAll` shows/hides tray icons without affecting `OverlayWindow`. Re-enable predicate: `shouldShow = !Dismissed && (RemoveAfter is null || RemoveAfter > now)` — prevents dismissed sessions from reappearing.

### Hover Dashboard (Phase 1)

`src/Imrdy.Windows/Dashboard/` contains `DashboardForm` (non-layered WinForms form; full child-control tree implemented — Label+Panel layout per spec mockup at 520 px width; `Form.Opacity` fade animation driven by `HoverDashboardController.OnDrainTick`; DWM mica/acrylic backdrop applied in `OnHandleCreated`; layout complete with visual seal PASSED on all 4 baseline fixtures via mockup-parity sub-plan; edge-case layout fixes applied: chip strip caps at `MaxVisibleChips=8` with a `+N more` overflow chip; footer uses a two-column `TableLayoutPanel` so keyboard hints (`↑↓`/`↵`/`Esc`) stay flush-right regardless of git-branch length; session-name label is 300 px wide with `AutoEllipsis=true` so long names truncate without reflowing the header; sparkline dark background matches form theme — no opaque-white rectangle on empty data) and `SparklineControl` (UserControl; `ReferenceTime` anchor property so fixture-preview paths render correctly — defaults to `DateTimeOffset.UtcNow` when unset; `DesignerSerializationVisibility.Hidden` on `Timestamps` to suppress WFO1000 build error; empty-state renders only the axis baseline — no placeholder that would draw a white fill). `HoverDashboardController` (sealed `IDisposable`; 200ms dwell timer + 300ms grace corridor + 12px bridge gap; create-and-dispose-per-show lifecycle; subscribes to `InteractiveOverlayWindow.SurfaceInteracted` to reset state on user interaction; Debug-level state-machine diagnostics). `TrayApp` instantiates `HoverDashboardController` on construction and wires/unwires it in `OnConfigChanged` and `ExitThreadCore`. `DashboardForm` is pinned to all virtual desktops via `IDesktopManager.PinWindowToAllDesktops` on show so it follows the user across desktops. Dev logging: `ImrdyPaths.DevBuildMarker` (`~/.imrdy/.dev-build`) — when this file exists, `ServiceRegistration.AddSerilog` sets minimum log level to Debug for all processes; `build-dev.sh` touches it after each deploy.

**Focus guard**: `DashboardForm.WndProc` intercepts `WM_MOUSEACTIVATE` and returns `MA_NOACTIVATE` when unpinned / `MA_ACTIVATE` when pinned (early return — no `base.WndProc` for that message, per Raymond Chen). Two-click pin-then-activate is a locked invariant: first body click fires `OnMouseDown → Pin()` WITHOUT `this.Activate()` so terminal focus is preserved; second click activates normally. `OnKeyDown` unpins + hides on Escape. `Pin()` / `Unpin()` / `IsPinned` are the only API around `_isPinned`.

**Post-interaction cooldown**: `HoverDashboardController._awaitingOverlayExit` is set true in `HandleSurfaceInteraction` (after user clicks overlay to activate a session) and cleared on the first drain tick where `cursorInOverlay == false`. While true, `OnDrainTick` short-circuits the dwell branch — prevents ghost re-show of the dashboard when the cursor remains on the overlay row after a click. Distinct from the 300ms grace corridor, which protects cursor traversal while the form is visible.

**Diagnostic heartbeat**: `HoverDashboardController` emits a state-dump Debug log every 10th drain tick (~1/sec when tray runs) with cursor position, overlay bounds, `cursorInOverlay`, `_awaitingOverlayExit`, `_dwellTicks`, `_wasInOverlayLastTick`, form-visible. Plus symmetric enter/exit-overlay transition logs, bounds-change log, and null-controller guard log in `TrayApp.OnHoverDrainTick`. Gated by the `.dev-build` marker; prod-silent.

### Live-Inspect IPC

`InspectIpcServer` (`src/Imrdy.Windows/Diagnostics/`) listens on `Local\ImrdyInspect` (named pipe). Two registered verbs: `"inspect-live"` (walker + analyzer, returns JSON control tree + `DiagnosticFinding` list) and `"render-live"` (DrawToBitmap → atomic PNG write). Architecture details:

- **Pipe name**: `Local\ImrdyInspect` (constant `ImrdyPaths.InspectPipeName`).
- **Protocol**: 4-byte little-endian length prefix + UTF-8 JSON body, both directions. Request: `InspectRequest(Verb, SessionId, OutputPath?)`. Response: `InspectResponse(SchemaVersion, Verb, Error?, Render?, Inspect?)`.
- **Concurrency**: 4 parallel `NamedPipeServerStream` accept loops (one `Task.Run` per slot); each loop creates a fresh server stream per connection and disposes it after. Max request body: 4 KiB.
- **Threading**: Each accepted request is dispatched to the UI thread via `Control.BeginInvoke` + `TaskCompletionSource` bridge. Handler has a 2-second budget; `TimeoutException` produces an error response rather than hanging the pipe.
- **Walker**: `InspectService` in `src/Imrdy.Windows/Diagnostics/` — WinForms-dependent, walks the live `DashboardForm` control tree on the UI thread, emits flat BFS-order `LayoutNode[]`. Stateless per-call.
- **Analyzer**: `LayoutAnalyzer` in `src/Imrdy.Core/Diagnostics/` — pure/stateless, no WinForms dependency. Four detectors: `regionClipRisk`, `siblingOverlap`, `edgeProximity`, `collapsedRow`. Produces `DiagnosticFinding` list with `ControlPath` (slash-separated), `Severity` (`info`/`warning`/`error`), `Details` dict.
- **DRY VM builder**: `LiveDashboardVmBuilder` (extracted from `HoverDashboardController`) builds `DashboardViewModel` from session state — shared by `HoverDashboardController`, `InspectLiveHandler`, and `RenderLiveHandler`.
- **Dev-default gate**: `DiagnosticsConfig.IpcEnabled` (`bool?` in `ImrdyConfig.Diagnostics`). Runtime resolution: `IpcEnabled ?? File.Exists(ImrdyPaths.DevBuildMarker)`. Null = on-in-dev, off-in-prod. Callers use the `?? File.Exists(...)` idiom directly; `EnsureDefaults` does NOT flatten it to a concrete bool (three-state semantics are intentional).
- **ACL**: `PipeSecurity` restricts to `WindowsIdentity.GetCurrent().User` with `FullControl`. ACL build failure logs a warning and skips server start rather than crashing the tray.
- **Lifecycle**: `TrayApp` instantiates and `Start`s `InspectIpcServer` during init, passes `_shutdownCts.Token`; accept loops self-terminate on cancellation. `Dispose` is a no-op (loops already winding down when called).
- **Schema versioning**: `schemaVersion: "1"`. Additive changes (new fields, new `kind` values) stay in v1. Breaking changes (field removal, type change, semantic change) require v2. See `docs/dashboard-inspect-schema.md` for the full JSON shape.

### Render Verb

`imrdy render <component> [inputs] [--output <path> | --output-dir <dir>]` produces in-process artifacts of imrdy UI surfaces. Phase 1 ships only the `dashboard` component — `DashboardForm` rendered from a `DashboardViewModel` fixture JSON, via `Form.DrawToBitmap` (no screen, deterministic, integration-test friendly). `imrdy render --list` enumerates registered components; `imrdy render --all --output-dir <dir>` renders every fixture of every component. Sequential execution on the main STA thread; SIGINT cancels between fixtures with exit 130.

Pure contracts (`IRenderableSurface`, `RenderContext`, `RenderResult`) live in `Imrdy.Core/Rendering/`; concrete renderers and the `RenderRegistry` live in `Imrdy.Windows/Rendering/` (WinForms-dependent). The `"render"` branch in `Program.cs` sits between `preview-dashboard` and the tray fallback, bypasses `Global\ImrdyMonitor` (same as preview-dashboard), and initialises WinForms before dispatching.

Protocol: for any UI-bearing change (DashboardForm, overlay, tray icons, menus) run `imrdy render --all` after a successful build and inspect every PNG before declaring work complete. A passing verifier wave is not a substitute for visual verification (see `~/.wiki-memory/verify-fix-loop-expert/platform-boundary-three-seal-gate.md`).

### Notification Dwell

`NotificationDwellState` in `Imrdy.Core/Sound/` gates toast and sound notifications behind per-status dwell timers. Icon updates remain immediate; notifications only fire after a session's status has "settled" for its dwell duration (2-5s depending on status). Per-session 10s toast cooldown provides additional backstop. Dwell check piggybacks on the existing 100ms drain timer — no new timer object. `CooldownTracker` (5s per-session sound cooldown) remains as defense-in-depth. `FiredNotification` record carries `PreviousStatus` and `NotificationType` for correct dispatch.

**Teammate-aware gating**: Hook events with `agent_id` (teammate/subagent activity) normally skip lead status updates — they only set `last_teammate_at` on the state file. Exception: when the lead status is "permission" and the teammate fires a permission-resolution event (PostToolUse, PostToolUseFailure, PermissionDenied), the permission is cleared to the derived status. `TeammateGate` in `Imrdy.Core/Hooks/` encapsulates this logic. Sessions with recent teammate activity (within 2 min) suppress `done→idle` dwell entry; instead, consensus promotion in the drain timer checks: when lead is `done` and no teammate activity for 15s (`TeammateQuietThreshold`), promotes to `idle` (green) + toast/sound. `idle_prompt` Notification (60s backstop) is also suppressed when teammates are active — keeps session at "done" (teal) and lets consensus handle promotion. Two speeds to green for teams: (1) consensus (~15s) when teammates go quiet (`TeammateQuietThreshold`), or (2) 90s `MaxDoneTime` status-time bypass (`ConsensusGate.IsEligibleForPromotion`, `entry.StatusSince`) when teammates pulse faster than the quiet threshold. Solo sessions: 5s dwell or 60s `idle_prompt` backstop. The 2-minute `TeammatePresenceTimeout` gates the `hasActiveTeammates` flag (suppression behaviors) but is NOT a promotion trigger. `ConsensusPromoted` flag on `SessionEntry` prevents duplicate promotions per done cycle. Sweep timer uses `LastProcessedTimestamp` on `SessionEntry` to skip re-processing unchanged state files.

## Build & Test

```bash
dotnet build                                    # Debug build
dotnet test --filter "Category!=Integration&Category!=Benchmark"  # Unit tests only (701 tests)
./build-dev.sh                                  # Publish → stop tray → deploy to ~/.local/bin/ → auto-respawn → touches ~/.imrdy/.dev-build (enables default-Debug dev logging)
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

**COM Virtual Desktop Interop**: Uses undocumented `IVirtualDesktopManagerInternal` with build-keyed GUIDs (`VirtualDesktopGuids.cs`). Gracefully degrades on unknown Windows builds. Recovers from Explorer restart via lazy re-init on COMException. `PinWindowToAllDesktops(IntPtr)` on `IDesktopManager`/`ComVirtualDesktop` uses raw vtable dispatch (`UnmanagedFunctionPointer` delegates, `PinningGuids` static class, `IApplicationView` as opaque `IntPtr`) — no `ComImport` interface, since pinning requires locating the `IApplicationViewCollection` vtable slot at runtime.

**Single Instance**: Mutex-gated via `MutexAcl.TryOpenExisting` (`Global\ImrdyMonitor`). Hook fast-path probes mutex to decide whether to spawn tray.

**Toast Notifications**: Uses `Microsoft.Toolkit.Uwp.Notifications` (WinRT toast API). Click activation fires on background thread — must marshal to UI via `BeginInvoke`. Extracts icon to `~/.imrdy/imrdy.png` for toast logo.

**Stop Signal**: Named `EventWaitHandle` (`Local\ImrdyStop`). `imrdy stop` signals it; tray listens on background thread, posts `ExitThread` to UI thread.

**Hook Logging**: `~/.imrdy/logs/hook_.log` with same rotation as monitor log (1MB, 5 retained files). Info-level: one line per hook event (`SessionId → Status (HookEvent)`). Debug-level raw payloads via `IMRDY_LOG=1`. Uses `shared: true` for concurrent hook process writes.

**IPC Dev/Prod Gating**: `DiagnosticsConfig.IpcEnabled` is `bool?` — three-state. Resolution rule: `IpcEnabled ?? File.Exists(ImrdyPaths.DevBuildMarker)`. Null → dev-default-on, off-in-prod (no `.dev-build` file in production). Do NOT flatten null to `false` in `EnsureDefaults` — the three-state semantics are intentional. Explicitly setting `diagnostics.ipcEnabled: true` in `config.json` enables IPC in production.

## Git Workflow

- **main**: releases, PR target
- **develop**: quick-fix lane — small, near-term changes; kept clean of long-running work
- **`imrdy-{name}`**: long-running feature arcs live on sibling worktrees with matching branch + dir name (e.g., `imrdy-gray` at `D:/dev/github/imrdy-gray`). One arc per branch. Periodically merge `develop` → `imrdy-{name}` to keep the arc current; reverse-merge `imrdy-{name}` → `develop` when the arc reaches a shippable milestone.
- Tags: `v*` for binary releases, `pack-*` for sound pack releases
