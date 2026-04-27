# imrdy-expert Wiki

## Pages

- [Hook Events](hook-events.md) — All 20 Claude Code hook events — what they send, status mapping, and real-world behavior
- [Teammate Detection](teammate-detection.md) — 4-layer teammate-aware notification system — deterministic gate, state tracking, consensus promotion, idle_prompt suppression
- [Notification Dwell](notification-dwell.md) — Dwell timer system that gates toast/sound behind status settling — prevents notification storms
- [Status Mapping](status-mapping.md) — Two-layer status mapping: hook event → base status → RGB color, with 9 base statuses
- [Architecture](architecture.md) — Seven entry points (hook, CommandRouter, preview-dashboard, render, inspect-live, render-live, tray), timer interactions, field preservation, and state file lifecycle
- [Overlay Interactivity](overlay-interactivity.md) — Passive/Interactive class split, `ISessionInteractionRouter` + `MenuAnchor` dispatch for all user actions, NCHITTEST click-through, 64-bit safe coordinate extraction, multi-monitor sign-extension
- [Hover Dashboard Form Lifecycle](hover-dashboard-form-lifecycle.md) — Non-layered DashboardForm: adaptive screen-aware anchoring + recreate-per-show for virtual desktop binding + IVirtualDesktopPinnedApps for persistence
- [Hover Dashboard State Machine](hover-dashboard-state-machine.md) — Hover preview controller: distinguish cursor traversal (grace corridor) from user commitment (click action) via SurfaceInteracted event
- [Dev Build Marker & Logging](dev-build-marker-logging.md) — Touch ~/.imrdy/.dev-build marker after dev deploys to enable Debug logging on all imrdy processes
- [Sparkline Reference Time](sparkline-reference-time.md) — SparklineControl requires ReferenceTime anchor (not UtcNow hardcoded) for correct rendering in both live and fixture-preview paths
- [WinForms Custom Property Serialization](winforms-custom-property-serialization.md) — UserControl public properties of non-serializable types require [DesignerSerializationVisibility(Hidden)] to avoid WFO1000 build error
- [xunit Parallel Console Redirects](xunit-parallel-console-redirect.md) — xunit v2 test classes compete over static Console.Out/Error state when running in parallel — use [Collection] attribute to serialize console-redirecting test classes
- [Render Verb Architecture](render-verb-architecture.md) — In-process PNG capture of WinForms surfaces: layer split (Core contracts / Windows registry+renderers), Program.cs branch placement, DrawToBitmap caveats, sequential STA execution, visual-seal fourth gate
- [TableLayoutPanel Row Toggle](tablelayoutpanel-row-toggle.md) — Absolute height 0 toggling is the canonical pattern for conditional row collapse — avoids FlowLayoutPanel+Dock=Fill zero-height silent failure
- [Persona Chip Horizontal Budget](persona-chip-horizontal-budget.md) — WinForms Anchor-based layouts: invisible-but-present sibling controls reduce available width for Anchor=Left|Right peers
- [DrawToBitmap Alpha Compositing](drawtobitmap-alpha-compositing.md) — Form.DrawToBitmap requires alpha ≥ 60–80 for visible decorative lines; design-system border (alpha ~8) invisible in static PNG render
- [DisplayItem vs SessionEntry Identity](display-item-vs-session-identity.md) — DisplayItem uses Id + ItemType; SessionEntry has SessionId. Choose based on whether filtering/visibility is needed.
- [HookAccumulationStore.Apply from FSW Path](hookaccumulationstore-apply-from-fsw.md) — Construct HookEventModel from StateFileModel when calling Apply from FileSystemWatcher callbacks.
- [Hover-Preview Live-Switch Detection](hover-switch-live-update.md) — Poll TryGetSessionIdAtScreenPoint while hover form is visible to refresh data when cursor moves between session icons.
- [Z-Order Gate Obsoletes Overlay-Hide-on-Menu](z-order-gate-obsoletes-menu-paper-over.md) — WindowFromPoint identity check eliminates need for stateful visibility toggles; pure Rectangle.Contains is insufficient.
- [Tray IPC: render-live and inspect-live verbs](inspect-ipc.md) — Named-pipe IPC server (`Local\ImrdyInspect`): protocol framing, dev-default gate, walker+analyzer architecture, threading model, and pipe ACL.

## Log
Last updated: 2026-04-27 | 21 pages | ingest boundary 10 (live-inspect step 09): added inspect-ipc.md; CLAUDE.md updated (seven entry points, IPC section, IpcEnabled constraint)
