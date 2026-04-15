---
tags: [imrdy/architecture]
updated: 2026-04-14
summary: "Three entry points, timer interactions, field preservation, and state file lifecycle"
---

# Architecture

## Three Entry Points (Program.cs)

| Command | Class | Purpose |
|---------|-------|---------|
| `imrdy hook` | HookCommand | Fast-path: read stdin JSON, derive status, write state file. No WinForms. Lightweight DI via HookServiceBuilder. |
| `imrdy <cmd>` | CommandRouter | CLI commands (status, packs, config, workspace, stop). Spectre.Console output. |
| `imrdy` | TrayApp | WinForms ApplicationContext. Application.Run with message pump. Full DI via MonitorServiceBuilder. |

The hook runs hundreds of times per session. It must be fast (~50ms). No COM, no WinForms initialization.

## State File Lifecycle

1. Hook writes `~/.imrdy/sessions/{session_id}.json` atomically
2. TrayApp's FileSystemWatcher detects change
3. Debounce timer (100ms drain) batches rapid changes
4. `HandleSessionFileChanged` reads state, updates icon/menu/overlay
5. Dwell timer gates toast/sound notifications (see [Notification Dwell](notification-dwell.md))

## Field Preservation

`FieldPreservation.PreserveFields()` carries sticky fields across state file writes. The hook writes a new state file on every event, but some fields are tray-owned and must survive:

- `SoundPack` — assigned by tray, not hooks
- `DesktopIndex` — assigned by tray
- `IconStyle` — assigned by tray or workspace
- `LastTeammateAt` — updated by teammate hooks, preserved by lead hooks

Pattern: `newState.Field ?? existing.Field` — new value wins if set, otherwise keep existing.

## Timer Interactions

TrayApp has multiple timers that interact:

| Timer | Interval | Purpose |
|-------|----------|---------|
| Drain timer | 100ms | Process pending file changes, dwell dispatch, consensus check |
| Sweep timer | 10s | Re-read all state files, detect stale/missing sessions |
| Stale timer | 60s | Remove sessions past grace period |

The drain timer is the central coordination point:
1. Process queued file change events
2. Dispatch fired dwell notifications
3. Run consensus promotion check (see [Teammate Detection](teammate-detection.md))

The sweep timer re-reads all state files but skips re-processing unchanged ones: `SessionEntry.LastProcessedTimestamp` is compared against the state file's `Timestamp` field. If they match, `HandleSessionFileChanged` returns early. This prevents redundant icon/dwell/notification processing on every sweep cycle.

## Session Icon Style Resolution

Chain: session override → workspace override (Cwd match) → global config

`ResolveSessionIconStyle()` implements this fallback. Renderer cache is keyed by style name. Changing a workspace's style refreshes all matching session icons.

## Single Instance

Mutex-gated via `Global\ImrdyMonitor`. Hook fast-path probes mutex to decide whether to spawn tray. `TraySpawner.EnsureRunning()` called from hook on every event.

## Stop Signal

Named `EventWaitHandle` (`Local\ImrdyStop`). `imrdy stop` signals it. Tray listens on background thread, marshals `ExitThread` to UI thread.
