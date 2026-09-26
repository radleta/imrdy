---
tags: [imrdy-expert/persistence]
summary: "Catalog of every place the tray process writes JSON state to disk — a debugging checklist for persistence loss"
last-verified: "2026-09-25"
---

# Tray Persistence Verbs

Use this page as the diagnostic checklist when a tray-side change "doesn't seem to be saved." Every tray-owned write path is here.

## Config — `~/.imrdy/config.json`

**Entry point:** `ConfigReader.Update(Func<ImrdyConfig, ImrdyConfig> mutate)` ([`ConfigReader.cs` `Update`](../../../src/Imrdy.Core/ConfigReader.cs))

**Pattern:** RMW with atomic write via `AtomicFileWriter`. Last-writer-wins. The tray is not the only writer: `imrdy config set` and `imrdy packs` call the same `ConfigReader.Update`, so a CLI write and a tray write can race and the later one wins whole-file.

**What triggers it:** Anything that mutates global config — controller menu actions (icon style, sound pack default, overlay toggle, etc.). Most paths go through `ConfigReader.Update(c => c with { ... })`.

**Failure modes:**
- The mutate lambda must produce a fully-formed `ImrdyConfig` — partial-record updates that drop nested objects (e.g., `c with { Tray = null }`) can fail `EnsureDefaults` round-tripping on the next read.
- Three-state nullable fields (`DiagnosticsConfig.IpcEnabled` = `bool?`) must NOT be flattened to a concrete `bool` in `EnsureDefaults`. The null state is semantically distinct from `false`.

## Workspaces — `~/.imrdy/workspaces.json`

**Entry points:** `WorkspaceStore` (`src/Imrdy.Core/Workspace/WorkspaceStore.cs`)

| Method | Effect |
|---|---|
| `Pin(path, name, desktop)` | Add or replace a workspace entry (preserves IconStyle if present) |
| `Unpin(path)` | Remove a workspace entry (no-op if absent) |
| `SetDesktop(path, desktop)` | Update desktop assignment (no-op if absent) |
| `SetIconStyle(path, iconStyle)` | Update icon-style override (null clears it; no-op if absent) |

**Pattern:** Each method does Load → mutate → Save. Save uses `AtomicFileWriter`. Last-writer-wins.

**What triggers it:** Workspace menu actions (pin/unpin via right-click on a tray dot, set workspace icon style via Manage submenu, etc.).

**Failure modes:**
- `Load()` catches `JsonException` and `IOException` and returns an empty `WorkspaceConfig`, and never writes on its own. A corrupt or mid-write file therefore appears as "no workspaces" — and a following `Pin` runs against that empty list and **overwrites** the original. (`Unpin`/`SetDesktop`/`SetIconStyle` find nothing and write nothing.)

## Cross-machine links — `~/.imrdy/publishers.json`

**Entry points:** `PublisherStore` (`src/Imrdy.Core/Publishing/PublisherStore.cs`) — shaped like `WorkspaceStore`.

| Method | Effect |
|---|---|
| `Upsert(entry)` | Write one whole record, replacing any link of the same name — the production save path |
| `Remove(name)` | Remove a link entry (no-op if absent) |
| `Add(name, endpoint)`, `SetDesktopIndex`, `SetMuted`, `SetEnabled` | Per-field setters with no production caller — tests only |

**Pattern:** Load → mutate → `Save` via `AtomicFileWriter`. Last-writer-wins.

**What triggers it:** the Connections window's Add… / Edit… / Remove buttons, routed through `IConnectionsHost.SavePublisher` / `RemovePublisher` on `TrayApp`, which calls `Upsert` (plus a `Remove` of the old name on a rename) and then reconciles the sink set. A new mutation surface must also trigger that reconcile — writing `publishers.json` alone leaves the live sinks stale (see the `PublisherStore.Upsert` doc comment).

**Registered in:** `MonitorServiceBuilder`, `DaemonServiceBuilder`, `CliServiceBuilder` — **never** `HookServiceBuilder`. The hook must not carry the publish stack.

**Failure modes:** same as `WorkspaceStore` — `Load()` swallows `JsonException`/`IOException`, returns an empty `PublisherConfig` and leaves the file alone (pinned by `Load_CorruptFile_ReturnsEmptyAndLeavesTheFileAlone`), so a corrupt file reads as "no links"; an `Upsert` after such a load replaces the file.

## Remote session state — `~/.imrdy/sessions/{session_id}.json` (receiver side)

**Entry point:** [`SessionIngest.cs`](../../../src/Imrdy.Core/Publishing/SessionIngest.cs) — the single writer of remote session files and the receiver's trust boundary (see [Cross-Machine Publishing](cross-machine-publishing.md#receiver-side)), shared by `FileSink` (writing into *another* machine's directory over a mount) and `WireListener` (writing into this machine's own).

**Pattern:** `RemoteSessionMerge` then `StateFileReader.WriteStateFileIfChanged` — a direct write, **never** a rename, since delete-then-move suppresses the receiver's FSW `Changed` event (same reason as [State File Write Path](state-file-write-path.md)), skipped when the merged bytes already match the file.

**Merge rule:** the *receiver* keeps `SoundPack`, `IconStyle` and `DesktopIndex`; an incoming `desktop_index` is discarded; `origin_machine` is stamped by the receiving side and never trusted from the payload.

**Failure modes:** a state file that already carries `origin_machine` must never be re-published — `SessionPublisher.IsLocallyOwned` is the guard, and losing it is an infinite publish loop between two machines rather than a dropped write.

## Session state — `~/.imrdy/sessions/{session_id}.json`

**Entry point:** `TrayApp.PersistSessionField(SessionEntry, Func<StateFileModel, StateFileModel>)` ([`TrayApp.cs` `PersistSessionField`](../../../src/Imrdy.Windows/TrayApp.cs))

**Pattern:** RMW with **non-atomic** `File.WriteAllBytes` via `StateFileReader.WriteStateFile`. **The hook process writes the same file concurrently** — this is the racy path. See [Tray vs Hook Write Race](tray-hook-write-race.md).

**Tray-owned fields written via this verb:**

| Wrapper | Field updated | Triggered by |
|---|---|---|
| `PersistSessionSoundPack(entry)` | `SoundPack` | Right-click session → Sound Pack submenu; also the new-session branch of `HandleSessionFileChanged`, which persists the resolved pack so the hook preserves it |
| `PersistSessionIconStyle(entry)` | `IconStyle` | Right-click session → Icon Style submenu; also the new-session branch |
| `PersistSessionDesktopIndex(entry)` | `DesktopIndex` | (1) Right-click session → "Assign to this Desktop" menu action. (2) WT auto-lock: new-session branch of `HandleSessionFileChanged` when `state.HookEvent == "SessionStart"` AND `entry.DesktopIndex is null` AND `IsWindowsTerminal(entry)` — captures `_desktopManager.GetCurrentDesktopIndex()` so the active desktop is remembered for the WT session. (3) Remote auto-assign: same branch, when `state.OriginMachine` is set AND `entry.DesktopIndex is null` AND (the origin is this box per `MachineNameResolver.IsSameMachine`, or its publisher has no `desktop_index`) — takes `RemoteDesktopDefault.Resolve`, which is the current desktop on a non-bootstrap `SessionStart`, else the most recently active same-machine sibling's desktop, else the current desktop. |

**Failure modes (in order of likelihood):**

1. **Race-loss vs hook event** — see [Tray vs Hook Write Race](tray-hook-write-race.md). Fixable only by adding the field to [Field Preservation Catalog](field-preservation-catalog.md).
2. **Tray reads a missing state file.** `PersistSessionField` reads the current file and silently skips the write if it returns `null` (deleted or corrupt) — no log line at all on that path.
3. **I/O failure on the write.** An `IOException` is caught and logged at Debug: `Could not persist session field for {SessionId}`.

**No success log:** `PersistSessionField` does not log on success. If you suspect a write is being lost, you cannot confirm from the log alone that the write happened — you must inspect the file timestamp or contents.

## Session removal — `~/.imrdy/sessions/{session_id}.json` (delete)

**Entry points:**
- `TrayApp.RemoveSession(sessionId)` — single-session removal triggered by sweep / SessionEnd grace expiry. Records the session's origin with `SessionPublisher.RecordOwnership` first (the publisher's removal guard needs it once the file is gone), then a direct `File.Delete` on the state file.
- `TrayApp.ClearAllSessions()` — manual menu action. Iterates and deletes each state file.
- `TrayApp.ClearSessionsFromMachine(name)` — Connections window → **Clear sessions**. Deletes every session that arrived under one publisher's name, routed through `RemoveSession` so icon, dwell state, cooldown and file go together. A *disconnected* publisher's sessions are never swept automatically — both cleanup paths key on the state file still existing on disk, and `WireListener` never deletes on disconnect, so this is the only way they go.
- `StateFileReader.RemoveStateFile(sessionsDir, sessionId)` — deletes the state file and any `.pid-{sessionId}` file beside it. Called by `SessionIngest` when a publisher sends a `remove`, not by the tray's own removal paths.

**Pattern:** Direct delete, swallows `IOException`. No atomicity needed — deletion is idempotent.

## What the tray does **not** write

Useful negative knowledge:

- **`.pid-{sessionId}` files** — nothing in `src/` writes them; `RemoveStateFile` only deletes one if an older build left it behind.
- **`logs/*.log`** — written by Serilog directly (rolling file sink). No tray-side persistence verb.
- **`graphics/packs/`** — read-only from the tray's perspective. Packs are installed by `packs install` CLI; the tray loads them on hot-reload.
- **`sound/packs/`** — same as graphics packs.
- **`imrdy.png`** — written once by the toast notification code (icon extraction). Not part of state.

## Cross-references

- [State File Write Path](state-file-write-path.md) — why session state is non-atomic
- [Tray vs Hook Write Race](tray-hook-write-race.md) — the writer-vs-writer hazard
- [Field Preservation Catalog](field-preservation-catalog.md) — the symmetry contract
- [Architecture](architecture.md) — State File Lifecycle
