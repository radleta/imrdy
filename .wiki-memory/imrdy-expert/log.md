# imrdy-expert Wiki — Operations Log

## [2026-04-27] update | Dashboard edge-fixes doc-updater sweep (boundary 12)
- Updated: CLAUDE.md Hover Dashboard (Phase 1) section — removed stale "Step 06 edge fixtures pending" note; captured five edge-case layout fixes: chip strip MaxVisibleChips=8 + "+N more" overflow chip; two-column TLP footer (keyboard hints flush-right); session-name 300px cap + AutoEllipsis; sparkline dark background; DashboardRenderer returns structured error on parse failure
- Verified: README.md — no dashboard layout internals; no change needed
- Verified: .wiki-memory/imrdy-expert/sparkline-reference-time.md — ReferenceTime anchor pattern still accurate; empty-state rendering not documented there; no change needed
- Verified: .wiki-memory/imrdy-expert/persona-chip-horizontal-budget.md — Controls.Remove pattern still accurate; not about chip overflow; no change needed
- Verified: .wiki-memory/imrdy-expert/render-verb-architecture.md — DashboardRenderer described at contract level only; no change needed
- Verified: .wiki-memory/imrdy-expert/hover-dashboard-form-lifecycle.md — form lifecycle patterns unchanged; no change needed
- Verified: docs/dashboard-inspect-schema.md — IPC schema unchanged; no change needed

## [2026-04-27] update | Live-inspect doc-updater safety-net sweep (boundary 11)
- Updated: README.md (CLI item 3: added inspect-live/render-live; CLI Commands block: added inspect-live/render-live rows; config schema: added diagnostics.ipcEnabled with three-state explanation)
- Updated: architecture.md (Five → Seven entry points table; added InspectLiveCommand + RenderLiveCommand rows; added thin-client note; added Diagnostics IPC Server section; updated frontmatter summary + date)
- Verified: index.md architecture summary line already correct ("Seven entry points") — no change needed
- Verified: CLAUDE.md Seven Entry Points and Live-Inspect IPC sections already accurate — no change needed
- Verified: docs/dashboard-inspect-schema.md already accurate (updated in step 09) — no change needed
- Verified: inspect-ipc.md already accurate (created in step 09) — no change needed
- Verified: tools/ directory has no README (no file exists there) — no change needed
- Verified: No HOOKS.md or CHANGELOG.md exists in the repo — no change needed

## [2026-04-27] ingest | Live-inspect step 09 source-doc rollup (boundary 10)
- New page: inspect-ipc.md (pattern: named-pipe IPC server `Local\ImrdyInspect`, 4-byte LE length-prefix framing, dev-default gate via `bool? IpcEnabled ?? File.Exists(DevBuildMarker)`, 4 parallel accept loops + UI-thread BeginInvoke + TCS bridge, walker+analyzer architecture, current-user ACL)
- Updated: index.md (+1 page, last updated 2026-04-27, boundary log line)
- Updated: CLAUDE.md (five→seven entry points, new Live-Inspect IPC architecture section, IpcEnabled Critical Constraint)
- Updated: docs/dashboard-inspect-schema.md (schema drift fixes: FormGeometry field names formX/formY/formWidth/formHeight, DiagnosticFinding fields controlPath+details replacing nodeIndex, severity includes "info", edgeProximity threshold 4px not 2px, collapsedRow trigger corrected to TableLayoutPanel row[N]==0, sample response corrected)
- Insight: `DiagnosticFinding` uses `controlPath` (slash-separated path string) not a flat `nodeIndex`. `FormGeometry` camelCase field names include the "form" prefix (`formX`, `formWidth`) — not the bare `x`/`width` names a reader might expect. The `edgeProximity` detector fires at 4px (`EdgeProximityFloor` constant), not 2px. `collapsedRow` triggers on `TableLayoutPanel.Details["row[N]"] == "0"`, not on any control with height 0. All four were schema-doc drift vs as-built code discovered during step 09.

## [2026-04-26] ingest | Overlay-dashboard-context step 08 final (boundary 10)
- New page: hover-switch-live-update.md (pattern: poll TryGetSessionIdAtScreenPoint while hover form visible to detect session changes; apply update in place without re-pin/opacity-reset)
- New page: z-order-gate-obsoletes-menu-paper-over.md (pattern: WindowFromPoint z-order gating eliminates stateful overlay-hide-on-menu logic; pure Rectangle.Contains insufficient when foreign topmost windows can cover the overlay)
- Updated: index.md (+2 pages, boundary boundary 10)
- Insight: Backend wiring iter-3-iter-9 chain revealed five progressive refinements to hover-detection lifecycle (cursor live-switch, bounds caching, z-order gating, form anchoring, post-interaction cooldown). First two apply to overlay-general patterns; three latter apply to overlay-adjacent hover-preview forms. WindowFromPoint z-order identity check is the canonical correctness gate.

## [2026-04-26] ingest | Overlay-dashboard-context step 08 completion (boundary 9)
- New page: display-item-vs-session-identity.md (gotcha: DisplayItem has Id+ItemType; SessionEntry has SessionId. Fleet projection simpler from SessionEntry when names needed.)
- New page: hookaccumulationstore-apply-from-fsw.md (pattern: construct HookEventModel from StateFileModel when calling Apply from FSW path; StateFileModel.HookEvent → HookEventModel.HookEventName)
- Ingested: step-08-displayitem-has-id-not-sessionid.md (gotcha)
- Ingested: step-08-hookaccumulationstore-apply-from-statefilemodel.md (pattern)
- Updated: index.md (+2 pages, last updated date, boundary log line)
- Insight: Backend wiring discovery — two distinct record types for display and session modeling. DisplayItem is filtered/visibility-aware (tray/overlay use); SessionEntry is full unfiltered state (dashboard/controller use). Mapping between them requires understanding both purposes. HookEventModel construction from StateFileModel is stable pattern for FSW-driven accumulator updates.

## [2026-04-25] update | Mockup-parity doc-updater safety-net sweep
- Updated: index.md (page count 15→16, boundary annotation 7→8)
- Updated: tablelayoutpanel-row-toggle.md (iter-7 MinimumSize=(w,0)+AutoSize invariant captured; Width-direct-set pattern was wrong — replaced with MinimumSize pattern; MaximumSize=Size.Empty constraint added; frontmatter summary updated)
- Updated: README.md (Overlay section: removed "5-second SetWindowPos watchdog" description — topmost is now Form.TopMost=true; replaced "no click interaction (full click-through)" with interactive default; added overlay.interactive config field to table; removed outdated V1 limitation)
- Verified: CLAUDE.md Hover Dashboard subsection accurate — "520 px width" matches FormMinWidth=520; test count 537 confirmed; layout description current
- Verified: architecture.md accurate — five entry points, timer table, field preservation all current
- Verified: hover-dashboard-form-lifecycle.md accurate
- Verified: render-verb-architecture.md accurate
- Verified: persona-chip-horizontal-budget.md accurate
- Verified: drawtobitmap-alpha-compositing.md accurate
- Verified: hover-dashboard-state-machine.md (not opened — state machine did not change in mockup-parity)

## [2026-04-25] ingest | Mockup-parity step 02 iter-8 completion (boundary 8)
- New page: drawtobitmap-alpha-compositing.md (gotcha: Form.DrawToBitmap composites onto opaque background; alpha<30 invisible; requires alpha≥60–80 for visible decorative lines vs design-system border alpha ~8)
- Ingested: step-02-drawtobitmap-alpha-compositing.md (gotcha)
- Updated: index.md (+1 page, last updated date, boundary log line)
- Insight: render-verb static PNG path differs from runtime DWM compositing in alpha composition. Design tokens (CSS var(--border) alpha ~8%) render correctly at runtime but vanish in DrawToBitmap output. All OnPaint decoration targeting visual-seal gate must override alpha to ≥60–80 independent of design tokens. Impacts future DashboardForm OnPaint work.

## [2026-04-25] ingest | Mockup-parity step 01 iter-7 completion (boundary 7)
- New page: persona-chip-horizontal-budget.md (gotcha: Controls.Remove required for dormant chips in Anchor layouts; Visible=false does not release width)
- Ingested: step-01-persona-chip-budget-stealing.md (gotcha)
- Updated: index.md (+1 page, last updated date, boundary log line)
- Insight: WinForms Anchor-based layout gotcha — invisible-but-present sibling controls with non-zero Width reduce available width for Anchor=Left|Right peers. Session name `overlay-dashboard-context` truncated to `overlav-dashboar...` at form Width=520 because dormant persona chip reserved fixed pixels. Two visual seal iterations were needed to isolate the root cause (budget-stealing, not label-width constraint). Pattern applies to all header chip-style controls — add to header layout checklist.

## [2026-04-25] ingest | Overlay-dashboard-context step 05 iter-5 completion (boundary 6)
- New page: tablelayoutpanel-row-toggle.md (gotcha: Absolute height 0 toggling is canonical; FlowLayoutPanel+Dock=Fill silently collapses to zero)
- Ingested: step-05-tablelayoutpanel-row-toggle-pattern.md (gotcha)
- Updated: index.md (+1 page, last updated date, boundary log line)
- Escalated: step-05-layout-regression-mental-walk.md (scope:user, target-domain:winforms-expert does not exist)
- Escalated: step-05-winforms-dual-mode-form-flag.md (scope:user, target-domain:winforms-expert does not exist)
- Insight: Layout-collapse bugs are silent — pass all verifier gates, only caught by visual gate or mental-walk. FlowLayoutPanel compute-preferred-size excludes Dock=Fill children, producing zero-height output in headless paths. TableLayoutPanel with Absolute row heights is the structural antidote — layout engine knows all row heights before layout pass.

## [2026-04-25] update | Render-verb boundary-2 doc-updater sweep
- Updated: architecture.md (Three → Five entry points; added preview-dashboard and render rows; updated frontmatter summary and date)
- Updated: index.md (architecture summary line: "Three entry points" → "Five entry points" with list)
- Updated: CLAUDE.md (unit test count 528 → 537 — render-verb added 9 new unit tests)
- Verified: render-verb-architecture.md already accurate (ingested in step 09)
- Verified: verify-fix-loop-expert/visual-seal-fourth-gate.md already accurate (ingested in step 09)
- Verified: no remaining references to tools/capture-preview.ps1 in any markdown file
- Verified: README.md has no entry-point table — no change needed

## [2026-04-25] ingest | Render-verb step 09 completion (boundary 5)
- New page: render-verb-architecture.md (layer split Core/Windows, Program.cs branch placement, DrawToBitmap caveats, sequential STA execution, mutex bypass rationale, inline DI rationale, visual-seal fourth gate)
- Ingested: render-verb-architecture.md (pattern)
- Updated: index.md (+1 page, last updated date, boundary log line)
- Insight: render verb makes visual verification practical — in-process PNG capture without screen or running tray. DrawToBitmap requires CreateControl+PerformLayout before capture; DWM mica does not render (GDI+ only). Visual seal is a mandatory fourth gate for any WinForms layout change; layout-collapse bugs pass all three verifier gates cleanly.

## [2026-04-24] ingest | Render-verb step 04 completion (boundary 4)
- New page: xunit-parallel-console-redirect.md (gotcha: xunit v2 parallel classes race on static Console.SetOut/Error; solution: [Collection] attribute serializes test classes)
- Ingested: step-04-xunit-console-redirect-collection.md (gotcha)
- Updated: index.md (+1 page, last updated date, boundary log line)
- Escalated: step-02-winforms-control-visible-ancestor-chain.md (scope:user, target-domain:winforms-expert does not exist in user .wiki-memory/)
- Insight: test parallelization race conditions are common when tests redirect process-wide state. xunit collection names provide a low-friction serialization barrier — no locks, no complex setup, just group names.

## [2026-04-24] ingest | Overlay-dashboard-context step 05 completion (boundary 3)
- New pages: sparkline-reference-time.md (ReferenceTime anchor pattern for time-windowed controls; distinguishes live from fixture-preview paths), winforms-custom-property-serialization.md (WFO1000 designer serialization fix via [DesignerSerializationVisibility(Hidden)])
- Ingested: step-05-sparkline-reference-time.md (pattern), step-05-winforms-designer-serialization-wfo1000.md (gotcha)
- Updated: index.md (+2 pages, last updated date, boundary log line)
- Escalated: step-05-csharp-object-initializer-post-method-invalid.md (scope:user, target-domain:csharp-expert does not exist)
- Insight: preview harness requires decoupled ReferenceTime from live wall-clock — controls become portable between fixture and production. WFO1000 is a designer-only constraint; property is usable at runtime. Both issues are step-05-specific (new SparklineControl + DashboardForm designer usage).

## [2026-04-24] ingest | Overlay-dashboard-context step 03 user manual gate (boundary 2)
- Updated: hover-dashboard-state-machine.md (added "Post-Interaction Cooldown" section: _awaitingOverlayExit flag pattern, ghost re-show problem statement, solution implementation, distinction from grace corridor)
- Learned file: step-03-post-interaction-cooldown.md (gotcha/pattern: hover-preview UIs need post-interaction cooldown to prevent click-dismiss ghost re-show)
- Insight: post-interaction cooldown and grace corridor are decoupled protective mechanisms for different state transitions — grace corridor shields visible→hidden, cooldown shields hidden→ready-to-show. The distinction is critical: grace corridor tolerates cursor drift during form display; cooldown prevents dwell re-trigger after user commitment.

## [2026-04-24] ingest | Overlay-dashboard-context step 02 risk-story gate
- New pages: hover-dashboard-form-lifecycle.md (adaptive anchor, recreate-per-show, PinView reference)
- New pages: hover-dashboard-state-machine.md (grace corridor, SurfaceInteracted event, dismiss-on-interaction pattern)
- New pages: dev-build-marker-logging.md (.dev-build marker enables Debug logging across process boundary)
- Synthesized from 6 learned files: step-02-adaptive-anchor.md, step-02-cross-desktop-visibility.md, step-02-cross-desktop-form-lifecycle.md, step-02-pinview-for-all-desktops.md, step-02-dismiss-on-interaction.md, step-02-dev-build-marker.md
- Updated: index.md (+3 pages, last updated date)
- Insight: hover dashboard on non-layered form requires three decoupled concerns (geometry, virtual desktop binding, state machine dismissal); each deserves its own page. Geometry-adjacent API reference (PinView) lives in lifecycle page rather than a separate gotcha page, because it's only applicable to persistence (not used in step 02).

## [2026-04-21] update | Interaction router + shutdown/auto-spawn fixes
- Updated: overlay-interactivity.md (frontmatter summary, "Architecture in One Sentence" now describes router dispatch, "Vanilla Right-Click" code sample uses `router.OpenSessionMenu` + `MenuAnchor.AtControl`, "NotifyIconMenuHost" section clarifies `ShowContextMenuAt` as sole branch point, new "Updates — 2026-04-21" section)
- Updated: index.md (overlay-interactivity summary line, Last updated 2026-04-21)
- Refactor: every user-initiated session/workspace interaction (tray click, overlay click, toast, controller menu, session menu) now routes through `ISessionInteractionRouter` with four methods — `ActivateSession`/`ActivateWorkspace` and `OpenSessionMenu`/`OpenWorkspaceMenu`. `MenuAnchor` value type (`AtTrayIcon` / `AtControl`) unifies the two right-click anchoring modes. `IOverlayClickRouter` deleted. `TrayApp.ShowContextMenuAt` is the single site where `NotifyIconMenuHost` vs `menu.Show` is chosen. Callers are forbidden from touching `SwitchToSessionDesktop`, `SwitchToWorkspaceDesktop`, `menu.Show`, or `NotifyIconMenuHost.Show` directly.
- Bug fix: `TrayApp.ListenForStopSignal` replaced `_controllerIcon.ContextMenuStrip?.BeginInvoke(ExitThread)` (which silently no-op'd when the menu strip handle was uncreated — i.e. for users who never right-clicked the controller icon) with `Application.Exit()`. TrayApp ctor now force-creates the controller strip `Handle` so toast click marshaling works for overlay-only users too.
- Bug fix: `HookCommand` removed `config.Tray.Enabled` gate from both auto-spawn sites (teammate + lead). Monitor process does more than render UI — state tracking, dwell timers, toasts, sounds, hot-reload — so it must be alive regardless of display surface. `IMRDY_NO_TRAY` env var remains as the headless escape hatch.
- Small: `InteractiveOverlayWindow` sets `Cursor = Cursors.Hand` in ctor — hand cursor only appears over icons because `WM_NCHITTEST` returns `HTTRANSPARENT` over gaps.
- Tests: 487 unit + 34 integration passing, incl. new `MenuAnchorTests.cs` (AtTrayIcon, AtControl, default guard).
- Insight: every surface-specific event handler had been duplicating the age-reset + icon-refresh logic inline. The router contract enforces a uniform two-phase shape — Mark, then Dispatch — and funnels all anchoring through one branch point. Adding a new surface is one call site; adding a new verb is one interface method with one implementation — all surfaces get it for free.

## [2026-04-19] update | Overlay vanilla refactor — drop NOACTIVATE on interactive, vanilla menu.Show
- Updated: overlay-interactivity.md (full rewrite — Passive/Interactive class split, no `WS_EX_NOACTIVATE` on interactive, vanilla `OnMouseDown`/`OnMouseUp` overrides, vanilla `menu.Show(owner, location)`, deleted `OverlayMenuPresenter` + `SetForegroundWindow`/`PostWmNull`/`ForceTopMost` band-aids, deleted topmost watchdog, `NotifyIconMenuHost` retained for tray-only path)
- Updated: index.md (summary line for overlay-interactivity)
- Insight: every band-aid (SetForegroundWindow, WM_NULL, ForceTopMost, OverlayMenuPresenter, NotifyIconMenuHost reflection from overlay) was compensating for one root cause — cutting WinForms out of its own menu pipeline by intercepting `WM_RBUTTONUP` and using owner-less `Show(Point)`. Going vanilla deleted ~150 lines.

## [2026-04-17] update | Overlay interactivity — Step 12 tray-overlay-parity refinements
- Updated: overlay-interactivity.md (64-bit safe lParam cast `(int)(nint)`, LOWORD/HIWORD sign-extension for multi-monitor negative coords, ScreenToClientPoint P/Invoke replacing Bounds subtraction, coordinate-space rules for WM_NCHITTEST vs WM_LBUTTONDOWN, OnlyInteractiveChanged fast-path, OverlayConfig.Interactive as bool? / STJ gotcha, dated Updates section)
- Learned files sweep: 2 files in scratch/tray-overlay-parity/learned/ — both already status:ingested from 2026-04-16; no new files to integrate

## [2026-04-16] ingest | Overlay interactivity research
- New page: overlay-interactivity.md (WS_EX_TRANSPARENT toggle pattern, NCHITTEST hit-testing, selective pass-through architecture)
- Updated: index.md (+1 page, last updated date)

## [2026-04-15] update | purple-sticking permission fix — TeammateGate
- Updated: teammate-detection.md (Layer 1 expanded: permission-clearing exception, TeammateGate class, ShouldClearPermission/ApplyTeammateEvent methods)
- Updated: hook-events.md (agent_id gate section: teammate events now delegate to TeammateGate, permission-clearing exception noted)
- Updated: CLAUDE.md (teammate-aware gating paragraph: unconditional "skip" replaced with exception for permission-resolution events, TeammateGate reference added)

## [2026-04-14] update | idle_prompt suppression + sweep optimization
- Updated: teammate-detection.md (3-layer → 4-layer, added Layer 4 idle_prompt suppression, revised speeds-to-green table)
- Updated: notification-dwell.md (expanded teammate-aware suppression section with idle_prompt detail)
- Updated: architecture.md (sweep timer 2s→10s, stale timer 30s→60s, added LastProcessedTimestamp skip optimization)
- Updated: hook-events.md (idle_prompt section clarified: solo-only backstop, suppressed for teams)
- Updated: status-mapping.md (done→idle promotion paths clarified for teams vs solo)
- Updated: index.md (teammate-detection summary updated to 4-layer)

## [2026-04-14] ingest | Initial seed from teammate-aware notifications session
- New page: hook-events.md (20 events mapped, behavioral discoveries from real testing)
- New page: teammate-detection.md (3-layer system, agent_id gate, consensus promotion, clawd-on-desk reference)
- New page: notification-dwell.md (dwell timers, defense-in-depth, teammate suppression)
- New page: status-mapping.md (event→status→color chain, "done" intermediate, aging tiers)
- New page: architecture.md (entry points, state lifecycle, field preservation, timer interactions)
- Index updated: +5 pages

## [2026-04-14] init | Wiki created
- Created: index.md, log.md, schema.md, .mditerc
- Domain registered in paths.env
