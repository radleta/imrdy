---
tags: [imrdy-expert/persistence]
summary: "Session state files use direct File.WriteAllBytes — not AtomicFileWriter — because delete-then-move suppresses FSW Changed events"
---

# State File Write Path

Three JSON surfaces are persisted under `~/.imrdy/`. Their write disciplines are **not symmetric**, and the asymmetry is intentional. Understanding which surface uses which path is required to reason about persistence bugs.

## The three persistence surfaces

| Surface | Path | Writer entry point | Atomic? |
|---|---|---|---|
| Config | `~/.imrdy/config.json` | `ConfigReader.Update(mutate)` | **Yes** — `AtomicFileWriter.Write` |
| Workspaces | `~/.imrdy/workspaces.json` | `WorkspaceStore.Save` (called by `Pin`, `Unpin`, `SetDesktop`, `SetIconStyle`) | **Yes** — `AtomicFileWriter.Write` |
| Session state | `~/.imrdy/sessions/{session_id}.json` | `StateFileReader.WriteStateFile` (called by `HookCommand` and `TrayApp.PersistSessionField`) | **No** — direct `File.WriteAllBytes` |

## Why session state files are non-atomic

`AtomicFileWriter` uses **delete-then-move**:

```csharp
File.WriteAllBytes(tmpPath, content);
if (File.Exists(path))
    File.Delete(path);
File.Move(tmpPath, path);
```

The comment in `AtomicFileWriter.cs` explains the reason: `File.Move(overwrite: true)` **suppresses FileSystemWatcher Changed events on Windows**. Delete-then-move guarantees the watcher fires a Created event reliably.

This works for `config.json` and `workspaces.json`, which the tray reads on Changed/Created and treats as authoritative replacements.

It is **wrong** for session state files. Those files are watched by `TrayApp.HandleSessionFileChanged` on a `Changed` event for an existing session — a Created-then-deleted-then-Created sequence inside one write would trigger spurious processing (and on a Created event for a missing-then-present file, the tray bootstraps as if a new session appeared). The session-state path therefore uses direct in-place `File.WriteAllBytes`, accepting partial-write risk on the reader side.

See `StateFileReader.WriteStateFile` (`src/Imrdy.Core/State/StateFileReader.cs`):

```csharp
// Direct write (not temp+rename) ensures FileSystemWatcher fires Changed events.
// The JSON reader handles partial reads gracefully, and files are small (~300 bytes).
File.WriteAllBytes(path, json);
```

## Reader-side mitigation for partial writes

`StateFileReader.ReadStateFile` catches both `JsonException` and `IOException`, returning `null` instead of throwing:

```csharp
try { return JsonSerializer.Deserialize(bytes, ImrdyJsonContext.Default.StateFileModel); }
catch (JsonException) { return null; }
catch (IOException)   { return null; }
```

The caller treats `null` as "missed this read" — the next FSW event will read the completed file. Files are ~300 bytes so partial-write windows are sub-millisecond.

This handles the **single-writer-vs-reader** race. It does **not** handle the **writer-vs-writer** race — see [Tray vs Hook Write Race](tray-hook-write-race.md).

## When direct write is bypassed for atomic write

Two cases use `AtomicFileWriter` even on Windows session-state-like data, deliberately:

- `ConfigReader.Update` — config changes are infrequent, and last-writer-wins on the whole file is acceptable.
- `WorkspaceStore.Save` — same reasoning. Workspace mutations are user-initiated (Pin/Unpin via menu), not high-frequency.

Both files have **one writer** (the tray process). Atomic writes are race-immune simply because no one else is writing.

## Inventory of write call sites

| Call site | Surface | Atomic? | Notes |
|---|---|---|---|
| `ConfigReader.cs:46` | config.json | Yes | RMW, last-writer-wins |
| `WorkspaceStore.cs:53` | workspaces.json | Yes | RMW, last-writer-wins |
| `HookCommand.cs:124` | session state (teammate path) | No | Updates `LastTeammateAt` only |
| `HookCommand.cs:210` | session state (lead path) | No | Full state file rewrite per hook event |
| `TrayApp.cs:845` | session state (`PersistSessionField`) | No | Updates one tray-owned field |
| `StateFileReader.cs:51` | session state (the primitive) | No | `File.WriteAllBytes` |

The single non-atomic file (session state) is also the **single file with two concurrent writers** — the hook process and the tray process. That combination is the source of the persistence-loss class of bugs documented in [Tray vs Hook Write Race](tray-hook-write-race.md).

## Cross-references

- [Tray vs Hook Write Race](tray-hook-write-race.md) — concurrent RMW race window between hook and tray writes on session state
- [Tray Persistence Verbs](tray-persistence-verbs.md) — full catalog of tray-owned write surfaces
- [Field Preservation Catalog](field-preservation-catalog.md) — the symmetry contract that mitigates the writer-vs-writer race
- [Architecture](architecture.md) — overview with State File Lifecycle section
