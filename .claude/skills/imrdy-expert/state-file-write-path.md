---
tags: [imrdy-expert/persistence]
summary: "Session state files use direct File.WriteAllBytes — not AtomicFileWriter — because delete-then-move suppresses FSW Changed events"
last-verified: "2026-09-25"
---

# State File Write Path

Four JSON surfaces are persisted under `~/.imrdy/`. Their write disciplines are **not symmetric**, and the asymmetry is intentional. Understanding which surface uses which path is required to reason about persistence bugs.

## The persistence surfaces

| Surface | Path | Writer entry point | Atomic? |
|---|---|---|---|
| Config | `~/.imrdy/config.json` | `ConfigReader.Update(mutate)` | **Yes** — `AtomicFileWriter.Write` |
| Workspaces | `~/.imrdy/workspaces.json` | `WorkspaceStore.Save` (called by `Pin`, `Unpin`, `SetDesktop`, `SetIconStyle`) | **Yes** — `AtomicFileWriter.Write` |
| Publisher links | `~/.imrdy/publishers.json` | `PublisherStore.Save` | **Yes** — `AtomicFileWriter.Write` |
| Session state | `~/.imrdy/sessions/{session_id}.json` | `StateFileReader.WriteStateFile` (called by `HookCommand` and `TrayApp.PersistSessionField`); `StateFileReader.WriteStateFileIfChanged` (called by `SessionIngest` for remote sessions) | **No** — direct `File.WriteAllBytes` |

## Why session state files are non-atomic

`AtomicFileWriter` uses **delete-then-move**:

```csharp
File.WriteAllBytes(tmpPath, content);
if (File.Exists(path))
    File.Delete(path);
File.Move(tmpPath, path);
```

The comment in [`AtomicFileWriter.cs`](../../../src/Imrdy.Core/AtomicFileWriter.cs) explains the reason: `File.Move(overwrite: true)` **suppresses FileSystemWatcher Changed events on Windows**. Delete-then-move guarantees the watcher fires a Created event reliably.

This works for `config.json`, `workspaces.json` and `publishers.json`, which the tray reads on Changed/Created and treats as authoritative replacements.

It is **wrong** for session state files. Those files are watched by `TrayApp.HandleSessionFileChanged` on a `Changed` event for an existing session — a Created-then-deleted-then-Created sequence inside one write would trigger spurious processing (and on a Created event for a missing-then-present file, the tray bootstraps as if a new session appeared). The session-state path therefore uses direct in-place `File.WriteAllBytes`, accepting partial-write risk on the reader side.

See [`StateFileReader.cs` `WriteStateFile`](../../../src/Imrdy.Core/State/StateFileReader.cs): its doc comment states the same reason — a direct write (not temp+rename) ensures FileSystemWatcher fires Changed events, and the reader tolerates partial reads of these small (~300 byte) files.

The remote-ingest writer uses the same direct write, and additionally skips the write when the merged bytes equal what is already on disk (`WriteStateFileIfChanged`): every direct write raises a `Changed` that travels the whole drain pipeline, and a TCP publisher re-sends its full snapshot on every reconnect.

## Reader-side mitigation for partial writes

`StateFileReader.ReadStateFile` catches both `JsonException` and `IOException`, returning `null` instead of throwing:

```csharp
try { return JsonSerializer.Deserialize(bytes, ImrdyJsonContext.Default.StateFileModel); }
catch (JsonException) { return null; }
catch (IOException)   { return null; }
```

The caller treats `null` as "missed this read" — the next FSW event will read the completed file. Files are ~300 bytes so partial-write windows are sub-millisecond.

This handles the **single-writer-vs-reader** race. It does **not** handle the **writer-vs-writer** race — see [Tray vs Hook Write Race](tray-hook-write-race.md).

## Why the other surfaces can be atomic

- `ConfigReader.Update` — written by the tray (controller menu) and by the CLI (`imrdy config set`, `imrdy packs`). Both are user-initiated and infrequent, so last-writer-wins on the whole file is acceptable.
- `WorkspaceStore.Save` and `PublisherStore.Save` — same reasoning. Mutations are user-initiated (menu actions, the Connections window), not high-frequency.

None of them is written by the hook, so none carries the hook-versus-tray race that session state does.

## Finding the write call sites

Rebuild the list rather than trusting a copy of it:

```bash
git grep -n 'AtomicFileWriter.Write\|WriteStateFile' -- src
```

Session state is the single non-atomic surface and also the **single surface with two concurrent local writers** — the hook process and the tray process. That combination is the source of the persistence-loss class of bugs documented in [Tray vs Hook Write Race](tray-hook-write-race.md). The hook's teammate path is not a full rewrite: it is an `existing with { … }` merge through `TeammateGate.ApplyTeammateEvent` that writes `Timestamp` and the roster and carries the lead's status forward; the lead path rewrites the whole file.

## Cross-references

- [Tray vs Hook Write Race](tray-hook-write-race.md) — concurrent RMW race window between hook and tray writes on session state
- [Tray Persistence Verbs](tray-persistence-verbs.md) — full catalog of tray-owned write surfaces
- [Field Preservation Catalog](field-preservation-catalog.md) — the symmetry contract that mitigates the writer-vs-writer race
- [Architecture](architecture.md) — overview with State File Lifecycle section
