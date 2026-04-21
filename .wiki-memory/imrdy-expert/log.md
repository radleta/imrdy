# imrdy-expert Wiki — Operations Log

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
