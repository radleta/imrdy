---
name: imrdy-expert
description: "imrdy project knowledge base — architecture decisions and behavior discovered from real usage."
---

You are an expert in the imrdy project — a Windows system tray monitor for Claude Code sessions (.NET 10, WinForms, single executable). Use the wiki below as your knowledge base. For deeper detail on any topic, use the Read tool on the linked pages.

## Pages

- [Architecture](architecture.md) — Seven entry points, timer interactions, field preservation, and state file lifecycle
- [State File Write Path](state-file-write-path.md) — Session state files use direct File.WriteAllBytes (not AtomicFileWriter) because delete-then-move suppresses FSW Changed events
- [Tray vs Hook Write Race](tray-hook-write-race.md) — Hook and tray both RMW session state files with no coordination — tray-side field changes are silently dropped if the field isn't on the FieldPreservation list
- [Tray Persistence Verbs](tray-persistence-verbs.md) — Catalog of every place the tray process writes JSON state to disk — debugging checklist for persistence loss
- [Field Preservation Catalog](field-preservation-catalog.md) — The 6 sticky fields, the merge pattern, and the symmetry contract every new tray-owned field must satisfy
- [WT Desktop Routing](wt-desktop-routing.md) — SwitchToSessionDesktop 3-step routing with WT-aware target resolution, compare-desktops focus guard against ping-pong, and SessionStart-only auto-lock
- [Hook Events](hook-events.md) — All 20 Claude Code hook events — what they send, status mapping, and real-world behavior
- [Teammate Detection](teammate-detection.md) — 4-layer teammate-aware notification system — deterministic gate, state tracking, consensus promotion, idle_prompt suppression
- [Notification Dwell](notification-dwell.md) — Dwell timer system that gates toast/sound behind status settling — prevents notification storms
- [Status Mapping](status-mapping.md) — Two-layer status mapping: hook event → base status → RGB color, with 9 base statuses
- [Overlay Interactivity](overlay-interactivity.md) — OverlayPanel single non-layered class (replaces former Passive/Interactive/Base split); context menus dispatched via ISessionInteractionRouter + MenuAnchor.AtControl(owner, location) — vanilla WinForms, no P/Invoke band-aids
- [Hover Dashboard Form Lifecycle](hover-dashboard-form-lifecycle.md) — HoverDashboardFormBase owns the shared shell (DWM, focus guard, pin/unpin, anchor placement); derived forms (SessionDashboardForm, WorkspaceDashboardForm) own their content panels — field-promote all dynamic controls for Update(vm) access
- [Hover Dashboard State Machine](hover-dashboard-state-machine.md) — HoverDashboardControllerBase owns the dwell/grace state machine; derived controllers plug in domain-specific dispatch (TryHitTestForOurDomain → BuildViewModel → CreateForm → ShowForm → ApplyViewModelUpdate); cross-controller hide protocol via FormShown event wired in TrayApp
- [Workspace Dashboard Architecture](workspace-dashboard-architecture.md) — WorkspaceDashboardForm + WorkspaceHoverDashboardController: BuildViewModel hit-index flow, VM-as-render-contract, live "ago" refresh, Update-refresh-all-fields pattern, GitInfo Ahead/Behind, cross-controller hide via FormShown
- [VM-as-Complete-Render-Contract](vm-as-complete-render-contract.md) — VM is the complete snapshot; builders take explicit "now" parameter; forms have zero clock reads; visual seal detected the clock-leak when workspace ActivityText diverged hours after baseline capture
- [WinForms Update Field-Promote](winforms-update-field-promote.md) — Field-promote all dynamic controls for Update(vm) access; BuildLayout/Update split; SetRowVisible for conditional rows; workspace→workspace stale-fields bug was caused by missing field promotion
- [Dev Build Marker & Logging](dev-build-marker-logging.md) — Touch ~/.imrdy/.dev-build marker after dev deploys to enable Debug logging on all imrdy processes; enables diagnostic traces without env var friction
- [Sparkline Reference Time](sparkline-reference-time.md) — SparklineControl requires a reference time anchor for correct rendering in live and fixture-preview paths
- [WinForms Custom Property Serialization](winforms-custom-property-serialization.md) — UserControl public properties of non-serializable types require DesignerSerializationVisibility attribute to avoid WFO1000 build error
- [xunit Parallel Console Redirects](xunit-parallel-console-redirect.md) — xunit v2 parallel test classes compete over Console.Out/Error redirects — use [Collection] attribute to serialize
- [Render Verb Architecture](render-verb-architecture.md) — imrdy render verb: in-process PNG capture of WinForms surfaces without a screen — layer split, Program.cs placement, DrawToBitmap caveats, sequential STA execution
- [TableLayoutPanel Row Toggle](tablelayoutpanel-row-toggle.md) — TableLayoutPanel row toggling via Absolute height 0; MinimumSize (not Width) pins fixed width with AutoSize=GrowAndShrink
- [Persona Chip Horizontal Budget](persona-chip-horizontal-budget.md) — WinForms Anchor-based layouts: invisible-but-present sibling controls reduce available width for Anchor=Left|Right peers
- [DrawToBitmap Alpha Compositing](drawtobitmap-alpha-compositing.md) — DrawToBitmap requires higher alpha for decorative lines than runtime DWM compositing
- [DisplayItem vs SessionEntry Identity](display-item-vs-session-identity.md) — DisplayItem uses Id + ItemType; SessionEntry has SessionId. Choose based on context.
- [HookAccumulationStore Apply from FSW](hookaccumulationstore-apply-from-fsw.md) — Construct HookEventModel from StateFileModel when calling Apply from FSW path.
- [Hover-Preview Live-Switch Detection](hover-switch-live-update.md) — Detect session change via TryGetSessionIdAtScreenPoint while form is visible; apply live-update pattern
- [Z-Order Gate Obsoletes Overlay-Hide-on-Menu](z-order-gate-obsoletes-menu-paper-over.md) — WindowFromPoint z-order gating makes overlay-hide-on-menu paper-overs redundant; pure geometric containment is insufficient
- [Tray IPC: render-live and inspect-live](inspect-ipc.md) — Tray IPC: render-live and inspect-live verbs — pipe protocol, dev-default gate, walker+analyzer, threading model, ACL
- [WSL→Windows PATH Passthrough Baseline](wsl-interop-baseline.md) — WSL→Windows PATH passthrough varies per distro; explicit verification needed
- [WSLENV Distro Identity Gap](wslenv-distro-not-forwarded.md) — WSLENV doesn't auto-forward WSL_DISTRO_NAME; Windows binaries can't self-identify source distro
- [WSL_DISTRO_NAME Env Var Gotcha](wsl-distro-env-var-gotcha.md) — WSL_DISTRO_NAME env var requires explicit pickup via IHookEnvironment; code exists but fallback was never wired
- [HookServiceBuilder Relocation](hook-service-builder-relocation.md) — HookServiceBuilder relocated from Imrdy.Windows.DI to Imrdy.Core.Hooks to enable cross-platform consumer access
- [overlay-rendering-internals](overlay-rendering-internals.md) — OverlayPanel OnPaint rendering; bitmap cache keyed by (style,status); aging via chip-background opacity ladder in OnPaint; Form.Bounds reliability on non-layered forms; monitor placement via OverlayConfig.Monitor
- [displayitem-source-gen-gotcha](displayitem-source-gen-gotcha.md) — ImrdyJsonContext must explicitly register DisplayItem and List<DisplayItem> for source-gen serialization
- [stj-source-gen-interface-caveat](stj-source-gen-interface-caveat.md) — STJ source-gen registers concrete List<T> but callers must query by concrete type, not interface
- [render-fixture-offscreen-pattern](render-fixture-offscreen-pattern.md) — Offscreen-Show pattern for deterministic Form fixture rendering in imrdy tests

## Meta

- [Operations Log](log.md) — Timestamped wiki operations log (ingest, lint, query filings)
- [Schema](schema.md) — Wiki conventions and page-type definitions
