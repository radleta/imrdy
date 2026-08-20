# imrdy

Windows system tray monitor for Claude Code sessions. .NET 10, WinForms, single executable.

## Why

Managing multiple Claude Code sessions in parallel is an attention problem: knowing which session needs you, which is working, which is idle, and acting on the right one without losing focus on your work.

imrdy puts that information in the system tray where it stays glanceable in peripheral vision:

- **Dots in the tray** — one icon per active session
- **Color = state** — busy, idle (green: waiting for you, nothing running), idle-with-agents-running (teal), needs attention, permission requested, error (tool/stop failures)
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
imrdy <command>                     → CommandRouter (status|packs|config|workspace|stop|inspect-live|render-live, Spectre.Console output)
imrdy preview-dashboard <fixture>   → PreviewDashboardCommand (standalone WinForms dev tool; inline ServiceCollection, bypasses mutex, deserializes DashboardViewModel fixture via ImrdyJsonContext, runs SessionDashboardForm pinned)
imrdy render <component> [args]     → RenderCommand (in-process UI artifact capture; bypasses mutex; placed between preview-dashboard and tray fallback)
imrdy inspect-live <id>             → InspectLiveCommand (CLI client; connects to tray via Local\ImrdyInspect pipe; prints/writes walker+analyzer JSON)
imrdy render-live <id> --output F   → RenderLiveCommand (CLI client; connects to tray via Local\ImrdyInspect pipe; captures live SessionDashboardForm PNG)
imrdy                               → TrayApp (WinForms ApplicationContext, Application.Run, message pump)
```

The hook runs hundreds of times per session. It uses `HookServiceBuilder` (lightweight DI, no COM/WinForms). The tray uses `MonitorServiceBuilder` (full DI with COM desktop manager). The preview-dashboard branch is placed between the Spectre CLI branch and the tray fallback — Spectre skips WinForms init; preview needs it. Mutex check is intentionally bypassed so preview runs alongside the real tray. The `inspect-live` and `render-live` CLI commands are thin clients — they send a request over the named pipe and print/write the response; all heavy work (walking, rendering) runs inside the already-running tray process on the UI thread.

### Graphics Packs

`ITrayIconRenderer` interface with two impls: `ParametricShapeRenderer` (built-in GDI+ shapes, always-available fallback; replaces `CircleIconRenderer`) and `PackIconRenderer` (SVG via Svg.NET v3.4.7). Six built-in styles: `circles`, `squares`, `triangles`, `diamonds`, `hexagons`, `plus`. `StyleNames` in `Imrdy.Core` provides `NormalizeStyleName` (maps `"dots"` → `"circles"`) and `BuiltInStyles`. Config flag `tray.iconStyle` selects: any built-in name or `"pack:<name>"`. `TrayIconRendererFactory` creates renderers by style name; `TrayApp._rendererCache` (keyed by style) replaces the former single `_renderer` field. Per-session icon style override: tray-owned, persisted via `PersistSessionField`, mirrors SoundPack pattern. Per-workspace icon style override: persisted via `WorkspaceStore.SetIconStyle`, `WorkspaceEntry.IconStyle` field in `workspaces.json`; workspace tray dots render with per-workspace style (global fallback when null). Session icon style resolution chain: session override → workspace override (matched via `Cwd` path) → global `_currentIconStyle`. Sessions with no explicit override inherit their workspace's style. `ResolveSessionIconStyle` in `TrayApp` implements this fallback; changing a workspace's icon style also refreshes all session icons. Packs live at `~/.imrdy/graphics/packs/<name>/` with a `pack.json` manifest. `GraphicsPackLoader` in `Imrdy.Core` mirrors the sound `PackLoader`. Pack load failure silently falls back to circles.

### Overlay (Mode B)

A single `OverlayPanel` class in `src/Imrdy.Windows/Overlay/` renders session icons as a horizontal row docked to the bottom screen edge. The former three-class hierarchy (`OverlayWindowBase` / `PassiveOverlayWindow` / `InteractiveOverlayWindow`) was collapsed and all three deleted. `OverlayPanel` is a non-layered WinForms `Form` (`WS_EX_TOOLWINDOW`, `TopMost = true`) with DWM mica backdrop and DWM native corner rounding (`DWMWCP_ROUND` via `ImrdyPalette.ApplyRoundedCorners`; GDI `Region` clip retained as Win10 ≤19045 fallback — `_usesDwmCorners` field tracks which path was taken), rendered via `OnPaint` (no `UpdateLayeredWindow`). Uses `GraphicsPackLoader` directly to render SVGs at overlay size; in built-in mode renders shapes via `ShapeDefinitions` delegates. Per-item icon style carried via `DisplayItem.IconStyle`. Per-chip bitmap cache keyed by `(style, status)` (tier-independent); aging is a chip-background opacity ladder applied in `OnPaint` (tier0 most opaque → tier4 faint plus a slight tier4 glyph dim) — not baked into the cached glyph. Config: `overlay.enabled`, `overlay.position`, `overlay.size`, `overlay.spacing` (default 8), `overlay.monitor` (int; selects which monitor to dock to), `overlay.locked` (bool; prevents drag repositioning; default false), `overlay.offsetX`/`overlay.offsetY` (int?; per-monitor free-float position in logical px from the target monitor's working-area origin; null falls back to `overlay.position`). `OverlayConfig.Interactive` was removed — the panel is always interactive (input handled via `OnMouseDown` / `OnMouseMove` / `OnMouseUp`; no `WM_NCHITTEST` override). WM_MOUSEACTIVATE always returns MA_NOACTIVATE — the overlay never steals foreground; terminal focus is preserved through drag and click.

`PInvokeOverlay.cs` in `src/Imrdy.Windows/Desktop/` retains `WS_EX_TOOLWINDOW`, `ScreenToClientPoint`, and `WindowAtPoint`; the layered-window P/Invokes (`UpdateLayeredWindow`, `DecodeLParamPoint`, `WS_EX_LAYERED` setup) were stripped. `RegisterWindowMessage` added for `TaskbarCreated` re-pin. No topmost watchdog — `Form.TopMost = true` is sufficient and re-asserting `HWND_TOPMOST` on a timer would clip any open menu.

`OverlayPanel` is recreated for structural config changes (Enabled/Size/Spacing); non-structural changes (Position/Monitor/Locked/OffsetX/OffsetY) apply in-place via `ApplyPositionConfig` — no flash, no dispose+recreate. A drag-in-flight guard defers any config reload until dragging ends (`_overlayReloadDeferred` flag).

**Shared display model**: `DisplayItem` / `DisplayItemCollection` in `Imrdy.Core/Display/` is the unified source of truth for both tray and overlay. `DisplayItemCollection.Build(inputs, trayEnabled)` produces a sorted, filtered list — pure data, no delegates (layer rule: no `System.Windows.Forms` in Core). Both `TrayApp` and `OverlayPanel` consume the same `IReadOnlyList<DisplayItem>` snapshot on each drain tick.

**ImrdyPalette**: `src/Imrdy.Windows/Theme/ImrdyPalette.cs` — shared theme helper extracted from `HoverDashboardFormBase`. Provides palette colors and `ApplyMica` / `ApplyRoundedRegion` / `ApplyRoundedCorners` helpers; consumed by `HoverDashboardFormBase`, `SessionDashboardForm`, `WorkspaceDashboardForm`, and `OverlayPanel`. `ApplyRoundedCorners(Form): bool` sets `DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_ROUND` (Win11+) and returns true on success; callers call `ApplyRoundedRegion` only when it returns false (Win10 fallback).

**Overlay interactivity**: The `WM_NCHITTEST` gap click-through policy was dropped (Decision 11). `OverlayPanel` has no `WM_NCHITTEST` override. Positioning is free-float: a left grip handle (6-dot glyph, `GripWidth = 14` logical px scaled by `DeviceDpi/96f`, dimmed by default → brightens on hover) is the sole drag-arming zone — chips and the gutter never arm a drag, only the grip does. `OnMouseDown` arms the drag FSM only when the grip is hit and the overlay is unlocked; `OnMouseMove` tracks movement (threshold-gated via `PInvokeOverlay.GetSystemMetricForDpi(SM_CXDRAG/SM_CYDRAG, DeviceDpi)` — DPI-aware, unlike the retired `SystemInformation.DragSize`); `OnMouseUp` completes — on a completed drag the panel drops at the release point, magnetically snaps to a working-area edge/corner within ~24 logical px, and clamps fully on-screen, then persists as per-monitor `overlay.offsetX`/`overlay.offsetY` (`overlay.position` remains a fallback anchor when no offset is set); on a non-drag release, left-click activates the session/workspace, right-click on a chip shows the session/workspace context menu, right-click on the gutter (no chip hit) calls `router.OpenOverlayMenu` (overlay settings submenu: 6 positions, spacing presets, per-monitor selector, Lock toggle — a position preset now writes the resolved offset via `OverlayPlacement.AnchorToOffset`, not a bare enum); a click on the grip or gutter with no drag is a no-op. The snap/offset/clamp/anchor math is pure `Imrdy.Core.Overlay.OverlayPlacement` (`System.Drawing.Primitives` only, no WinForms). `ScreenToClientPoint` P/Invoke is still used for DPI-correct screen→client conversion in the hover-highlight poll and hit-testing (`Bounds`-subtraction is wrong above 100% scale). The passive (fully click-through) variant is removed — use `overlay.enabled: false` to suppress the overlay entirely.

**Interaction router**: `ISessionInteractionRouter` (`src/Imrdy.Windows/Interaction/`) is the single entry point for every user-initiated session/workspace interaction, regardless of surface — tray `NotifyIcon.MouseClick`, overlay `OnMouseDown`/`OnMouseUp`, toast activation, controller menu "Switch to X" items. Five methods: `ActivateSession(id)` / `ActivateWorkspace(path)` for primary (left-click) intents; `OpenSessionMenu(id, MenuAnchor)` / `OpenWorkspaceMenu(path, MenuAnchor)` for session/workspace right-click; `OpenOverlayMenu(MenuAnchor)` for overlay gutter right-click (no chip hit) — opens the overlay settings submenu built by `OverlayMenuBuilder` (6 positions, spacing presets, per-monitor selector, Lock toggle). `TrayApp` is the sole implementation; every method follows the same two-phase shape — `MarkSessionInteracted`/`MarkWorkspaceInteracted` resets `LastSeenAt` + refreshes icon, then dispatches the intent. **Call sites MUST NOT call `SwitchToSessionDesktop`, `SwitchToWorkspaceDesktop`, `menu.Show`, or `NotifyIconMenuHost.Show` directly from event handlers** — everything routes through the interface so age-reset and icon-brighten are uniform. Adding a new surface means one call site; adding a new verb means one interface method with one implementation — all surfaces get it for free.

**MenuAnchor**: `MenuAnchor` value type encapsulates the two anchoring modes for right-click menus. `MenuAnchor.AtTrayIcon(NotifyIcon)` dispatches via `NotifyIconMenuHost` (reflection-based private `NotifyIcon.ShowContextMenu`, required because the shell's tray notification context isn't compatible with vanilla `menu.Show`). `MenuAnchor.AtControl(Control, Point)` dispatches via the standard owner-based `ContextMenuStrip.Show(Control, Point)` overload — used by the overlay since its activatable form satisfies the WinForms foreground/hover-hot-track anchor requirement naturally (no `SetForegroundWindow`/`WM_NULL`/`ForceTopMost` band-aids). `TrayApp.ShowContextMenuAt` is the single routing function; `NotifyIconMenuHost` and `menu.Show` are not referenced outside it.

**Overlay context menus**: `OverlayPanel.OnMouseUp` (vanilla WinForms event override — NOT `WM_RBUTTONUP` interception) dispatches right-click by hit result: chip hit → `router.OpenSessionMenu/OpenWorkspaceMenu(id, MenuAnchor.AtControl(this, e.Location))`; no chip hit (gutter) → `router.OpenOverlayMenu(MenuAnchor.AtControl(this, e.Location))`. WinForms then handles foreground transfer, hover hot-tracking, dismissal, and `ToolStripManager.ModalMenuFilter` integration internally.

**Tray god toggle**: `TrayConfig.Enabled` (default `true`). `TrayApp` caches `_trayEnabled` at ctor and updates it in `OnConfigChanged`; `ApplyTrayEnabledToAll` shows/hides tray icons without affecting `OverlayPanel`. Re-enable predicate: `shouldShow = !Dismissed && (RemoveAfter is null || RemoveAfter > now)` — prevents dismissed sessions from reappearing.

### Hover Dashboard (Phase 1 — Session + Workspace)

`src/Imrdy.Windows/Dashboard/` contains two dashboard peers built on a shared base/derived split:

**Base classes** (shared shell, no domain knowledge):
- `HoverDashboardFormBase` — abstract WinForms `Form`; owns: `FormBorderStyle.None`, `TopMost`, rounded `Region` clip (no DWM mica — dashboards fade via `Form.Opacity` on a layered window; mica on a layered form composites white into GDI-clipped corners), `WM_MOUSEACTIVATE` focus guard (`MA_NOACTIVATE` when unpinned / `MA_ACTIVATE` when pinned), `Pin()`/`Unpin()`/`IsPinned` API, Escape key handler (`OnKeyDown` unpins + hides), adaptive screen-aware anchor-edge placement (`PlaceWithAnchor`, screen-aware above/below flip, multi-monitor X clamp). Form shell width = 520 px (`FormMinWidth`). `FormatDuration` is a thin delegating wrapper to `RelativeTimeFormatter` in `Imrdy.Core.Time`. Palette colors (`BgForm`, `FgPrimary`, `FgSecondary`, `FgMuted`, `BgFooter`) sourced from `ImrdyPalette` (extracted theme helper in `src/Imrdy.Windows/Theme/`). `BridgeGap` (12 px) declared as `protected static`.
- `HoverDashboardControllerBase` — abstract `IDisposable`; owns: dwell/grace/dismissal state machine (200ms dwell, 300ms grace corridor, 12px bridge gap), `Form.Opacity` fade animation (+0.5/-0.5 per tick), create-and-dispose-per-show form lifecycle, diagnostic heartbeat (Debug log every 10th tick), `FormShown` event (raised after `TryShowForm` completes — peer controller subscribes via TrayApp to call `HideIfVisible`), `HideIfVisible()` (idempotent, triggers existing fade-out). Abstract dispatch chain: `TryHitTestForOurDomain` → `BuildViewModel` → `CreateForm` → `ShowForm` → `ApplyViewModelUpdate`. Extension points: `OnSameItemRefreshTick`, `OnFormShown`, `OnFormHidden`.

**P6 — TrayApp owns all subscription wiring**: the base ctor does NOT subscribe to `OverlayPanel.SurfaceInteracted`; TrayApp calls both controllers' `HandleSurfaceInteraction()` directly. The cross-controller hide protocol (`FormShown += peer.HideIfVisible`) is also wired by TrayApp, NOT by the base ctor or derived ctors.

**Session peer** (`SessionDashboardForm` + `SessionHoverDashboardController`):
- `SessionDashboardForm` (non-layered; Label+Panel layout at 520 px; chip strip `MaxVisibleChips=8` + `+N more` overflow; two-column footer `TableLayoutPanel` with keyboard hints flush-right; 300 px session-name label with `AutoEllipsis=true`; sparkline dark background). `SparklineControl` (UserControl; `ReferenceTime` anchor property defaults to `DateTimeOffset.UtcNow` when unset; `DesignerSerializationVisibility.Hidden` to suppress WFO1000; empty-state renders only axis baseline).
- `SessionHoverDashboardController`: `TryHitTestForOurDomain` filters by `DisplayItemType.Session`; `BuildViewModel` calls `LiveDashboardVmBuilder.BuildForSession`; `OnSameItemRefreshTick` override calls `RebuildAndApplyUpdate` every `RefreshIntervalTicks=10` (~1s) for live session-state refresh; `OnFormShown` kicks off async git fetch; `OnFormHidden` clears `_hoveredSessionId`.

**Workspace peer** (`WorkspaceDashboardForm` + `WorkspaceHoverDashboardController`):
- `WorkspaceDashboardForm`: header (Name + Desktop chip + Path + IconStyle chip), activity row, conditional git row (`SetRowVisible` toggles height 0 ↔ 36 when `Git` is null), footer. All dynamic controls are class fields (field-promote pattern); `Update(vm)` is the sole content source and refreshes every dynamic field. `SetRowVisible(rowIndex, visible, height)` toggles `TableLayoutPanel` `RowStyle.Height` — same pattern as `SessionDashboardForm`.
- `WorkspaceHoverDashboardController`: `TryHitTestForOurDomain` filters by `DisplayItemType.Workspace`; `BuildViewModel` calls `WorkspaceStore.Load()` per build (no cache — YAGNI) then `WorkspaceDashboardViewModelBuilder.Build(entry, git, currentDesktopIndex, lastSeenAt, now)`; `OnSameItemRefreshTick` override rebuilds VM with fresh `DateTimeOffset.UtcNow` every ~1s so the `ActivityText` "ago" string advances while visible.

**VM-as-complete-render-contract**: `WorkspaceDashboardViewModel` carries `ActivityText` (precomputed "active Xh Ym ago" or "never seen" string). `WorkspaceDashboardViewModelBuilder.Build` takes an explicit `DateTimeOffset now` parameter — pure function, deterministic for visual seal tests. `WorkspaceDashboardForm` has zero clock reads.

**`RelativeTimeFormatter`** in `Imrdy.Core.Time` — pure Core utility; `HoverDashboardFormBase.FormatDuration` is a thin delegating wrapper.

**Cross-controller hide protocol**: `HoverDashboardControllerBase.FormShown` is raised at end of `TryShowForm` after `OnFormShown` returns. `HideIfVisible()` is idempotent and reuses the fade-out path. TrayApp wires: `_sessionController.FormShown += () => _workspaceController.HideIfVisible()` and vice-versa. Result: session→workspace or workspace→workspace traversal always shows exactly one dashboard.

**Focus guard**: `HoverDashboardFormBase.WndProc` intercepts `WM_MOUSEACTIVATE` and returns `MA_NOACTIVATE` when unpinned / `MA_ACTIVATE` when pinned (early return — no `base.WndProc` for that message, per Raymond Chen). Two-click pin-then-activate is a locked invariant: first body click fires `OnMouseDown → Pin()` WITHOUT `this.Activate()` so terminal focus is preserved; second click activates normally. `OnKeyDown` unpins + hides on Escape. `Pin()` / `Unpin()` / `IsPinned` are the only API around `_isPinned`.

**Post-interaction cooldown**: `HoverDashboardControllerBase._awaitingOverlayExit` is set true in `HandleSurfaceInteraction` (after user clicks overlay to activate a session/workspace) and cleared on the first drain tick where `cursorInOverlay == false`. While true, `OnDrainTick` short-circuits the dwell branch — prevents ghost re-show of the dashboard when the cursor remains on the overlay row after a click. Distinct from the 300ms grace corridor, which protects cursor traversal while the form is visible.

**Diagnostic heartbeat**: `HoverDashboardControllerBase` emits a state-dump Debug log every 10th drain tick (~1/sec when tray runs) with cursor position, overlay bounds, `cursorInOverlay`, `_awaitingOverlayExit`, `_dwellTicks`, `_wasInOverlayLastTick`, form-visible. Plus symmetric enter/exit-overlay transition logs, bounds-change log, and null-controller guard log in `TrayApp.OnHoverDrainTick`. Gated by the `.dev-build` marker; prod-silent. Dashboard forms pinned to all virtual desktops via raw `IVirtualDesktopPinnedApps` vtable dispatch on show.

### Live-Inspect IPC

`InspectIpcServer` (`src/Imrdy.Windows/Diagnostics/`) listens on `Local\ImrdyInspect` (named pipe). Two registered verbs: `"inspect-live"` (walker + analyzer, returns JSON control tree + `DiagnosticFinding` list) and `"render-live"` (DrawToBitmap → atomic PNG write). Architecture details:

- **Pipe name**: `Local\ImrdyInspect` (constant `ImrdyPaths.InspectPipeName`).
- **Protocol**: 4-byte little-endian length prefix + UTF-8 JSON body, both directions. Request: `InspectRequest(Verb, SessionId, OutputPath?)`. Response: `InspectResponse(SchemaVersion, Verb, Error?, Render?, Inspect?)`.
- **Concurrency**: 4 parallel `NamedPipeServerStream` accept loops (one `Task.Run` per slot); each loop creates a fresh server stream per connection and disposes it after. Max request body: 4 KiB.
- **Threading**: Each accepted request is dispatched to the UI thread via `Control.BeginInvoke` + `TaskCompletionSource` bridge. Handler has a 2-second budget; `TimeoutException` produces an error response rather than hanging the pipe.
- **Walker**: `InspectService` in `src/Imrdy.Windows/Diagnostics/` — WinForms-dependent, walks the live `SessionDashboardForm` control tree on the UI thread, emits flat BFS-order `LayoutNode[]`. Stateless per-call.
- **Analyzer**: `LayoutAnalyzer` in `src/Imrdy.Core/Diagnostics/` — pure/stateless, no WinForms dependency. Four detectors: `regionClipRisk`, `siblingOverlap`, `edgeProximity`, `collapsedRow`. Produces `DiagnosticFinding` list with `ControlPath` (slash-separated), `Severity` (`info`/`warning`/`error`), `Details` dict.
- **DRY VM builder**: `LiveDashboardVmBuilder` (extracted from `HoverDashboardController`) builds `DashboardViewModel` from session state — shared by `HoverDashboardController`, `InspectLiveHandler`, and `RenderLiveHandler`.
- **Dev-default gate**: `DiagnosticsConfig.IpcEnabled` (`bool?` in `ImrdyConfig.Diagnostics`). Runtime resolution: `IpcEnabled ?? File.Exists(ImrdyPaths.DevBuildMarker)`. Null = on-in-dev, off-in-prod. Callers use the `?? File.Exists(...)` idiom directly; `EnsureDefaults` does NOT flatten it to a concrete bool (three-state semantics are intentional).
- **ACL**: `PipeSecurity` restricts to `WindowsIdentity.GetCurrent().User` with `FullControl`. ACL build failure logs a warning and skips server start rather than crashing the tray.
- **Lifecycle**: `TrayApp` instantiates and `Start`s `InspectIpcServer` during init, passes `_shutdownCts.Token`; accept loops self-terminate on cancellation. `Dispose` is a no-op (loops already winding down when called).
- **Schema versioning**: `schemaVersion: "1"`. Additive changes (new fields, new `kind` values) stay in v1. Breaking changes (field removal, type change, semantic change) require v2. See `docs/dashboard-inspect-schema.md` for the full JSON shape.

### Render Verb

`imrdy render <component> [inputs] [--output <path> | --output-dir <dir>]` produces in-process artifacts of imrdy UI surfaces. Two registered components: `dashboard` (`SessionDashboardForm` rendered from a `DashboardViewModel` fixture JSON) and `overlay` (`OverlayPanel` rendered from one of four overlay fixture files), both via `Form.DrawToBitmap` (no screen, deterministic, integration-test friendly). `imrdy render --list` enumerates registered components; `imrdy render --all --output-dir <dir>` renders every fixture of every component. Sequential execution on the main STA thread; SIGINT cancels between fixtures with exit 130.

Pure contracts (`IRenderableSurface`, `RenderContext`, `RenderResult`) live in `Imrdy.Core/Rendering/`; concrete renderers and the `RenderRegistry` live in `Imrdy.Windows/Rendering/` (WinForms-dependent). The `"render"` branch in `Program.cs` sits between `preview-dashboard` and the tray fallback, bypasses `Global\ImrdyMonitor` (same as preview-dashboard), and initialises WinForms before dispatching.

Protocol: for any UI-bearing change (SessionDashboardForm, WorkspaceDashboardForm, overlay, tray icons, menus) run `imrdy render --all` after a successful build and inspect every PNG before declaring work complete. A passing verifier wave is not a substitute for visual verification (see `~/.wiki-memory/verify-fix-loop-expert/platform-boundary-three-seal-gate.md`).

### Notification Dwell

`NotificationDwellState` in `Imrdy.Core/Sound/` gates toast and sound notifications behind per-status dwell timers. Icon updates remain immediate; notifications only fire after a session's status has "settled" for its dwell duration (2-5s depending on status). Per-session 10s toast cooldown provides additional backstop. Dwell check piggybacks on the existing 100ms drain timer — no new timer object. `CooldownTracker` (5s per-session sound cooldown) remains as defense-in-depth. `FiredNotification` record carries `PreviousStatus` and `NotificationType` for correct dispatch.

**Lead-readiness gating**: the stored status (`StateFileModel.Status`) answers exactly one question — *is the main session waiting for the user?* Only the lead's own hook events (those **without** `agent_id`) may answer it. `Stop` is the authoritative signal: a lead `Stop` means the main agent finished its turn and is waiting. Empirically (1341 hook events, Aug 2026) every lead `Stop` is followed by either `Notification/idle_prompt` or `UserPromptSubmit` — never by more lead work — so `Stop → idle`.

Subagent events (`agent_id` present) **never** move the lead's status. They only refresh `last_teammate_at`, which drives icon aging so a session whose lead is blocked inside a long `Task` call does not dim. The one exception is `TeammateGate.ShouldClearPermission`: a subagent may clear a lead `permission` it resolved (PostToolUse, PostToolUseFailure, PermissionDenied). Subagent *lifecycle* events (SubagentStart/Stop, TaskCreated/Completed, TeammateIdle) can reach the lead stream without an `agent_id` because the parent spawns and reaps the subagent, so `TeammateGate.IsSubagentLifecycleEvent` filters them on the lead path too and carries the existing status forward.

This replaced a teammate busy-promotion + consensus-promotion + idle_prompt-suppression system that assumed subagent activity implied the lead was working. Modern Claude Code runs background agents that keep working after the lead returns control, so that assumption produced permanently-`busy` sessions that were in fact waiting for input. Sweep timer uses `LastProcessedTimestamp` on `SessionEntry` to skip re-processing unchanged state files.

**Display resolution (`DisplayStatus`, render-time only)**: the stored status answers "is the lead waiting?", which alone overloads green — a waiting lead with background agents may resume *itself*, since a completing background agent delivers a `<task-notification>` as a synthetic `UserPromptSubmit` (measured: 7 of 10 `UserPromptSubmit` events on one session were agent-driven). `DisplayStatus.Resolve(status, lastTeammateAt, now)` therefore renders an `idle` lead as `"done"` (teal) while subagent activity is within 2 minutes, so green means *nothing is running*. The 2-minute window is measured, not guessed: across 1085 inter-event gaps from 45 agents, p99 = 75s and only 0.09% exceed 120s, versus 19.6% exceeding 15s.

This is **display-only** — writing teal back into `StateFileModel.Status` would make `Resolve` stop seeing an idle lead and freeze the session at teal, so the dwell-fired status write-back in `TrayApp` was removed. Icons/overlay/tooltip read `SessionEntry.EffectiveStatus`; `State.Status` stays the lead's truth. The teal → green flip is time-driven and announced by no hook, so `OnDrainTimerTick` recomputes `Resolve` every 100ms against `SessionEntry.LastEffectiveStatus` and drives both icon and dwell from that edge — making the drain tick the single dwell driver for status changes, which is what keeps teal silent (not in `DefaultToastEvents`) and fires the toast + `SoundEvent.Finished` on green.

## Build & Test

```bash
dotnet build                                    # Debug build
dotnet test --filter "Category!=Integration&Category!=Benchmark"  # Unit tests only (763 tests: 750 Core + 13 Windows)
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
- **develop**: active development
- Tags: `v*` for binary releases, `pack-*` for sound pack releases
