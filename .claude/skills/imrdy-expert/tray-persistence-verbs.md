---
tags: [imrdy-expert/persistence]
summary: "Catalog of every place the tray process writes JSON state to disk — a debugging checklist for persistence loss"
---

# Tray Persistence Verbs

Use this page as the diagnostic checklist when a tray-side change "doesn't seem to be saved." Every tray-owned write path is here.

## Config — `~/.imrdy/config.json`

**Entry point:** `ConfigReader.Update(Func<ImrdyConfig, ImrdyConfig> mutate)` (`src/Imrdy.Core/ConfigReader.cs:41`)

**Pattern:** RMW with atomic write via `AtomicFileWriter`. Last-writer-wins. No concurrent writers in production (tray is the only writer of config.json).

**What triggers it:** Anything that mutates global config — controller menu actions (icon style, sound pack default, overlay toggle, etc.). Most paths go through `ConfigReader.Update(c => c with { ... })`.

**Failure modes:**
- The mutate lambda must produce a fully-formed `ImrdyConfig` — partial-record updates that drop nested objects (e.g., `c with { Tray = null }`) can fail `EnsureDefaults` round-tripping on the next read.
- Three-state nullable fields (`OverlayConfig.Interactive` = `bool?`, `DiagnosticsConfig.IpcEnabled` = `bool?`) must NOT be flattened to a concrete `bool` in `EnsureDefaults`. The null state is semantically distinct from `false`.

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
- `Load()` catches `JsonException` and `IOException` and returns an empty `WorkspaceConfig`. A corrupt or mid-write file appears as "no workspaces" — Pin/Unpin then runs against an empty list and **overwrites** the original. (Unlikely in practice since workspaces.json is atomic-write — but worth knowing for diagnosis.)

## Session state — `~/.imrdy/sessions/{session_id}.json`

**Entry point:** `TrayApp.PersistSessionField(SessionEntry, Func<StateFileModel, StateFileModel>)` (`src/Imrdy.Windows/TrayApp.cs:837`)

**Pattern:** RMW with **non-atomic** `File.WriteAllBytes` via `StateFileReader.WriteStateFile`. **The hook process writes the same file concurrently** — this is the racy path. See [Tray vs Hook Write Race](tray-hook-write-race.md).

**Tray-owned fields written via this verb today:**

| Wrapper | Field updated | Triggered by |
|---|---|---|
| `PersistSessionSoundPack(entry)` | `SoundPack` | Right-click session → Sound Pack submenu |
| `PersistSessionIconStyle(entry)` | `IconStyle` | Right-click session → Icon Style submenu |
| `PersistSessionDesktopIndex(entry)` | `DesktopIndex` | (1) Right-click session → "Assign to this Desktop" menu action. (2) WT auto-lock: new-session branch of `HandleSessionFileChanged` when `state.HookEvent == "SessionStart"` AND `entry.DesktopIndex is null` AND `IsWindowsTerminal(entry)` — captures `_desktopManager.GetCurrentDesktopIndex()` so the active desktop is remembered for the WT session. |

**Failure modes (in order of likelihood):**

1. **Race-loss vs hook event** — see [Tray vs Hook Write Race](tray-hook-write-race.md). Fixable only by adding the field to [Field Preservation Catalog](field-preservation-catalog.md).
2. **Tray reads a missing state file.** `PersistSessionField` reads the current file and exits silently if it returns `null` (deleted or corrupt). The tray's intended mutation is dropped with only a `LogDebug` line. Look for `Could not persist session field for {SessionId}` in the log.
3. **Silent no-op on session removal mid-write.** If the session has been swept (state file deleted) between the tray's user action and the persist call, the file is gone and the write is silently skipped.

**Instrumentation gap:** `PersistSessionField` does not log on success. Only failures emit a Debug-level line. If you suspect a write is being lost, you cannot confirm from the log alone that the write happened — you must inspect the file timestamp or contents.

## Session removal — `~/.imrdy/sessions/{session_id}.json` (delete)

**Entry points:**
- `TrayApp.RemoveSession(sessionId)` — single-session removal triggered by sweep / SessionEnd grace expiry. Direct `File.Delete` on the state file.
- `TrayApp.ClearAllSessions()` — manual menu action. Iterates and deletes each state file.
- `StateFileReader.RemoveStateFile(sessionsDir, sessionId)` — utility that deletes both the state file and the `.pid-{sessionId}` cache file.

**Pattern:** Direct delete, swallows `IOException`. No atomicity needed — deletion is idempotent.

**Failure modes:** None observed. `File.Delete` on a missing path is a no-op; locked file (rare) is caught and logged.

## What the tray does **not** write

Useful negative knowledge:

- **`.pid-{sessionId}` cache files** — written by the hook (`HookCommand`), not the tray. Tray only deletes them on session removal via `StateFileReader.RemoveStateFile`.
- **`logs/*.log`** — written by Serilog directly (rolling file sink). No tray-side persistence verb.
- **`graphics/packs/`** — read-only from the tray's perspective. Packs are installed by `packs install` CLI; the tray loads them on hot-reload.
- **`sound/packs/`** — same as graphics packs.
- **`imrdy.png`** — written once by the toast notification code (icon extraction). Not part of state.

## Cross-references

- [State File Write Path](state-file-write-path.md) — why session state is non-atomic
- [Tray vs Hook Write Race](tray-hook-write-race.md) — the writer-vs-writer hazard
- [Field Preservation Catalog](field-preservation-catalog.md) — the symmetry contract
- [Architecture](architecture.md) — State File Lifecycle
