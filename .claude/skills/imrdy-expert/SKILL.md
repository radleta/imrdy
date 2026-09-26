---
name: imrdy-expert
wiki: true
description: "imrdy project knowledge base — architecture decisions and behavior discovered from real usage."
---

You are an expert in the imrdy project — a Windows system tray monitor for Claude Code sessions (.NET 10, WinForms, single executable). Use the wiki below as your knowledge base. For deeper detail on any topic, use the Read tool on the linked pages.

## Pages

<!-- BEGIN:PAGES -->
- [Architecture](architecture.md) — Seven entry points, timer interactions, field preservation, and state file lifecycle
- [State File Write Path](state-file-write-path.md) — Session state files use direct File.WriteAllBytes — not AtomicFileWriter — because delete-then-move suppresses FSW Changed events
- [Config Live Reload](config-live-reload.md) — config.json FSW routes through OnConfigChanged for full live reload (sound + icon style + tray god toggle + overlay + network); overlay structural-delta: Position/Monitor/Locked/OffsetX/OffsetY apply in-place, Enabled/Size/Spacing recreate; network re-resolves machineName always and rebinds the listener only on a ListenEnabled/ListenPort change; startup uses LoadSoundConfig separately
- [Tray vs Hook Write Race](tray-hook-write-race.md) — Hook and tray both RMW session state files with no coordination — tray-side field changes are silently dropped if the field isn't on the FieldPreservation list
- [Tray Persistence Verbs](tray-persistence-verbs.md) — Catalog of every place the tray process writes JSON state to disk — a debugging checklist for persistence loss
- [Field Preservation Catalog](field-preservation-catalog.md) — The sticky fields in FieldPreservation.PreserveFields, why each is preserved, the merge pattern, and the symmetry contract every new tray-owned field must satisfy
- [WT Desktop Routing](wt-desktop-routing.md) — SwitchToSessionDesktop 3-step routing: resolve target → switch desktop → guarded focus. WT skipped from dynamic lookup; ForceForeground guarded against ping-pong; auto-lock on SessionStart only
- [Hook Events](hook-events.md) — All 20 Claude Code hook events — what they send, status mapping, the background_tasks roster on Stop/SubagentStop and its type-dependent entry shape, how to grep the tasks= token without reading your own echo, and real-world behavior
- [Teammate Detection](teammate-detection.md) — How imrdy reads the background_tasks roster Claude Code sends: the agent_id gate keeps subagents from moving lead status, Stop/SubagentStop supply running_tasks, and DisplayStatus.Resolve renders an idle lead with a non-empty roster as teal
- [Notification Dwell](notification-dwell.md) — Dwell timer system that gates toast/sound behind status settling — prevents notification storms
- [Status Mapping](status-mapping.md) — Status mapping: hook event → status → base status → RGB color, plus the display-only teal done status and icon aging tiers
- [Overlay Interactivity](overlay-interactivity.md) — Overlay input: only the grip arms a drag, the threshold is per-monitor-DPI, a drop snaps and persists per-monitor offsets, there is no click-through mode; DragCompleted and SurfaceInteracted carry separate contracts wired by TrayApp
- [Hover Dashboard Form Lifecycle](hover-dashboard-form-lifecycle.md) — HoverDashboardFormBase owns the shared shell (rounded Region, focus guard, pin/unpin, anchor placement, all-desktops pinning); derived forms (SessionDashboardForm, WorkspaceDashboardForm) own their content panels — field-promote all dynamic controls for Update(vm) access
- [Hover Dashboard State Machine](hover-dashboard-state-machine.md) — HoverDashboardControllerBase owns the dwell/grace state machine; derived controllers plug in domain-specific dispatch (TryHitTestForOurDomain → BuildViewModel → CreateForm → ShowForm → ApplyViewModelUpdate); cross-controller hide protocol via FormShown event wired in TrayApp
- [Workspace Dashboard Architecture](workspace-dashboard-architecture.md) — WorkspaceDashboardForm + WorkspaceHoverDashboardController: BuildViewModel hit-index flow, VM-as-render-contract, live 'ago' refresh, Update-refresh-all-fields pattern, GitInfo Ahead/Behind, cross-controller hide via FormShown
- [VM-as-Complete-Render-Contract](vm-as-complete-render-contract.md) — VM-as-complete-render-contract: builders take an explicit 'now' parameter and precompute every display string, so a form that reads no clock renders a fixture to the same PNG at any hour — SessionDashboardForm.Update is the exception and derives its time labels from UtcNow
- [WinForms Update Field-Promote](winforms-update-field-promote.md) — Field-promote all dynamic WinForms controls for Update(vm) access; BuildLayout/Update split; SetRowVisible for conditional rows; chip list clear+rebuild — a dynamic control left as a local shows stale fields on switch
- [Dev Build Marker & Logging](dev-build-marker-logging.md) — build-dev.sh writes ~/.imrdy/.dev-build (holding the repo root) after every dev deploy; every imrdy process that sees it logs at Debug, the inspect pipe defaults on, and imrdy render resolves fixtures and output from its path
- [Sparkline Reference Time](sparkline-reference-time.md) — SparklineControl requires a reference time anchor for correct rendering in live and fixture-preview paths
- [WinForms Custom Property Serialization](winforms-custom-property-serialization.md) — UserControl public properties of non-serializable types require DesignerSerializationVisibility attribute to avoid WFO1000 build error
- [Render Verb Architecture](render-verb-architecture.md) — imrdy render verb: in-process PNG capture of WinForms surfaces without a screen — layer split, Program.cs placement, the offscreen-Show capture sequence, output layout, sequential STA execution
- [TableLayoutPanel Row Toggle](tablelayoutpanel-row-toggle.md) — TableLayoutPanel row toggling via Absolute height 0; MinimumSize (not Width) pins fixed width with AutoSize=GrowAndShrink
- [Dormant Controls in Anchor Layouts](dormant-controls-anchor-layout.md) — WinForms Anchor-based layouts: invisible-but-present sibling controls reduce available width for Anchor=Left|Right peers
- [DrawToBitmap Alpha Compositing](drawtobitmap-alpha-compositing.md) — DrawToBitmap renders very-low-alpha decorative lines invisible — dashboard border and separator lines use alpha 80, not the 20-alpha Border constant
- [DisplayItem vs SessionEntry Identity](display-item-vs-session-identity.md) — DisplayItem uses Id + ItemType and is the filtered, sorted tray/overlay snapshot; SessionEntry has SessionId and full session state — choose by whether visibility filtering matters
- [Tray IPC: render-live, inspect-live and links-live](inspect-ipc.md) — Tray IPC: the render-live, inspect-live and links-live verbs — pipe protocol, dev-default gate, walker+analyzer, threading model, ACL
- [WSL Interop](wsl-interop.md) — WSL and imrdy: Windows PATH passthrough varies per distro, a Windows imrdy.exe run from WSL cannot see WSL_DISTRO_NAME, and wsl_distro comes only from the Linux hook's IHookEnvironment — carried on the state file but never rendered
- [overlay-rendering-internals](overlay-rendering-internals.md) — OverlayPanel OnPaint rendering; bitmap cache keyed by (style,status,disconnected); aging via chip-background opacity ladder in OnPaint; the empty-state placeholder chip; Form.Bounds reliability on non-layered forms; TopMost with no watchdog; placement through mutable fields and OverlayPlacement
- [Source-Generated JSON Registration](source-gen-json-registration.md) — ImrdyJsonContext serializes only the root types it registers — register each one (List<DisplayItem> included), and reach it through the generated typed property or the concrete type, never an interface, or the published binary reads null silently
- [internals-visible-to-mechanism](internals-visible-to-mechanism.md) — Imrdy.Core grants InternalsVisibleTo to assembly named 'imrdy' (the Imrdy.Windows project) — not 'Imrdy.Windows'. Internal Core classes (e.g. ControllerMenuModel) are accessible from Windows code without making them public. Use 'imrdy' (lowercase) as the assembly-name key in any future InternalsVisibleTo grant from Core.
- [Overlay Context Menus](overlay-context-menus.md) — How the overlay's right-click ContextMenuStrip opens reliably and hands focus back: MA_NOACTIVATE always, an explicit SetForegroundWindow + InvokeWithForegroundAttached grant, PostMessage(WM_NULL) per KB135788, and a continuously sampled restore target
- [WinForms Menu Tests](testing/winforms-menu-tests.md) — MenuRenderer.Apply asserts Application.MessageLoop, which a bare STA thread does not satisfy — ContextMenuStrip.Opening tests need a real Application.Run pump or the assert is swallowed as a misleading zero-items failure
- [build-dev-cross-platform](build-dev-cross-platform.md) — build-dev.sh OS-detects and publishes a Linux binary to ~/.local/bin/imrdy — atomic swap via temp-in-same-dir + mv, and a daemon stop sequence that escalates SIGTERM to SIGKILL and then refuses to deploy (exit 1) rather than relaunching over a daemon it could not stop
- [ConfigValidator Known Keys](config-validator-known-keys.md) — ConfigValidator keeps its own known-keys sets, compiler-unenforced — a new config.json section is a three-touch change (ImrdyConfig, EnsureDefaults, ConfigValidator), and no test notices a skipped third touch on its own
- [com-virtual-desktop-interop](com-virtual-desktop-interop.md) — IVirtualDesktopManagerInternal: probe a newest-first IID candidate list and dispatch with the accepted IID's own vtable layout — never pick the IID by build number or key slots off 'is Windows 11'
- [tray-icon-rendering](tray-icon-rendering.md) — Tray icon renderers: ParametricShapeRenderer and PackIconRenderer behind ITrayIconRenderer, built per style by TrayIconRendererFactory — 'dots' is the config default and must be normalized before comparing, and a bad pack or unknown style silently renders circles
- [cross-machine-publishing](cross-machine-publishing.md) — Cross-machine publishing map: sinks and the endpoint-is-direction rule, the wire listener's pre-auth guards, the ownership guard that fails closed, the snapshot's Skip/Publish/Retire decision with no age term, the two hosts, and receiver-side ingest and activation
- [publisher-liveness](publisher-liveness.md) — When a remote session reads as disconnected: a TCP publisher answers from its inbound link, a file-sink publisher from its heartbeat file, a missing beat reads as connected, nothing is persisted — plus heartbeat format, deploy order, and the geometric disconnected glyph
- [connections-surfaces](connections-surfaces.md) — The Connections window and imrdy links on both binaries render one ConnectionsViewModel; imrdy links exits 1 only on live health from the tray; ConnectionsForm is deliberately not a hover-dashboard peer
<!-- END:PAGES -->

## Meta

- [Schema](schema.md) — Wiki conventions and page-type definitions
