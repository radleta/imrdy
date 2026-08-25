---
tags: [imrdy-expert/persistence]
summary: "Hook and tray both RMW session state files with no coordination — tray-side field changes are silently dropped if the field isn't on the FieldPreservation list"
---

# Tray vs Hook Write Race

The session state file at `~/.imrdy/sessions/{session_id}.json` has **two independent writers**:

- **Hook process** (`HookCommand`) — fires on every Claude Code hook event (potentially hundreds per session). Full state file rewrite each time.
- **Tray process** (`TrayApp.PersistSessionField`) — fires on user actions (per-session sound pack assignment, per-session icon style override, etc.). Single-field mutation.

Neither writer coordinates with the other. Both follow a read-modify-write pattern against the same file. This is the **structural** source of the "tray changes don't fully persist" bug class — a single data file is the shared mutable state for two systems that don't know about each other.

## The race window

```
T0  Tray.PersistSessionField reads file        → state X (SoundPack=null)
T1  Hook process reads file                    → existing = state X (SoundPack=null)
T2  Hook builds newState from event fields
T3  Tray writes file                           → state X' (SoundPack="Y")
T4  Hook applies PreserveFields(newState, existing)
      → because hook's `existing` snapshot was X (pre-tray-write),
        PreserveFields uses SoundPack=null from `existing`
T5  Hook writes file                           → state X'' (SoundPack=null)
                                                  ← TRAY MUTATION LOST
```

The tray successfully wrote its change to disk. The very next hook event then overwrote it, because the hook's RMW used a snapshot taken before the tray wrote.

## What protects against this today

[`FieldPreservation.PreserveFields`](field-preservation-catalog.md) — the hook's write merges `newState` with `existing` using `newState.Field ?? existing.Field`. This works **only if the tray's write also landed in `existing`** — i.e., the hook reads after the tray writes. But the race window above shows it can also fail: when the hook's read happens **before** the tray's write, `existing` is stale.

Wait — re-read carefully. The merge uses `newState.Field ?? existing.Field`. The hook's `newState` does not set `SoundPack` (only the tray writes that field). So the merge resolves to `null ?? existing.SoundPack`. If `existing` was read **after** the tray wrote, the result is the tray's value (correct). If `existing` was read **before** the tray wrote, the result is `null` — the previous value, not the tray's new value (incorrect).

So `PreserveFields` doesn't fully eliminate the race. It eliminates it only for the case where the tray's write happens **outside the hook's RMW window** — i.e., when no hook event is in flight. For the bug to fire, a hook event must be in flight, and the tray must write its update inside that hook's RMW window.

How likely is that? Every hook event runs the full hook process — read stdin, derive status, read state file, write state file. That's a 50–200 ms window. Hook events fire every few seconds during active use. Tray mutations are rare (user actions). The window is small but non-zero, and the consequence (silent loss) makes it worth treating as a real hazard.

## What is and isn't covered by PreserveFields

`PreserveFields` only protects fields explicitly listed:

```csharp
return newState with
{
    SoundPack       = newState.SoundPack       ?? existing.SoundPack,
    DesktopIndex    = newState.DesktopIndex    ?? existing.DesktopIndex,
    IconStyle       = newState.IconStyle       ?? existing.IconStyle,
    StartedAt       = newState.StartedAt       ?? existing.StartedAt,
    WslDistro       = newState.WslDistro       ?? existing.WslDistro,
    RunningTasks    = newState.RunningTasks    ?? existing.RunningTasks,
};
```

Note the last entry: `RunningTasks` serialises as `running_tasks` on disk and is populated from the `background_tasks` roster the hook payload carries. It is the one preserved field where an *empty* value is meaningful — `[]` means "measured: nothing is running" and must overwrite the previous roster. The `??` already does the right thing (an empty list is non-null), but it means this field is racy in one extra direction the others are not: a stale `existing` snapshot can resurrect a roster a later event had already emptied — and on the tray side, a tray RMW begun before an emptying hook write lands resurrects the prior roster the same way.

If a future tray feature adds a new persisted field — say `entry.PreferredVoice` — and writes it via `PersistSessionField` **without** also adding `PreferredVoice` to the `PreserveFields` list, every hook event will silently overwrite it with `null`. This is the **drift hazard**.

The drift is silent because the tray write log shows success and the data lands on disk. The loss happens on the next hook event, which is logged separately.

See [Field Preservation Catalog](field-preservation-catalog.md) for the current list and the symmetry test that detects drift.

## How to diagnose a suspected race-loss incident

1. **Enable Debug logging** via the dev-build marker (`~/.imrdy/.dev-build` — see [Dev Build Marker & Logging](dev-build-marker-logging.md)).
2. Look in `~/.imrdy/logs/imrdy_.log` and `~/.imrdy/logs/hook_.log`.
3. For the affected session, find:
   - Tray write event: `Could not persist session field` (failure) or no log on success — `PersistSessionField` does not log on success today (instrumentation gap).
   - Hook write event: `State file written: {Path}` (Debug).
4. Inspect the on-disk state file before and after each hook event. Any field that was set by the tray but is `null` after a subsequent hook event is a race-loss candidate.
5. Check the [Field Preservation Catalog](field-preservation-catalog.md): if the field is **not** on the list, it is structurally racy and will be lost on every hook event regardless of timing.

## Architectural framing

This is a **shared-data-source** anti-pattern. Two systems (hook, tray) treat one file as their working state with no mediator. The race is inherent to that shape.

Alternatives that would eliminate the race class:

- **Separate file per writer.** Tray-owned fields move to a sibling `~/.imrdy/sessions/{session_id}.tray.json` written only by the tray. The reader merges at read time. Eliminates writer-vs-writer races (each file has one writer). Doubles the FSW surface.
- **Single writer with hook-to-tray IPC.** Hook becomes write-only stdin → tray; tray is the sole writer of session state. Eliminates the race entirely. Requires the tray to be running for the hook to make progress (today the hook can write even before the tray exists).
- **Lock + re-read on hook write.** Hook acquires an exclusive lock, re-reads `existing` after the lock is held, then writes. Closes the race window deterministically but adds a serialization point that the lock-free design intentionally avoids.

None of these are proposed here — this page documents the hazard, not the fix.

## Cross-references

- [State File Write Path](state-file-write-path.md) — why the file is non-atomic in the first place
- [Field Preservation Catalog](field-preservation-catalog.md) — the symmetry contract that mitigates this race
- [Tray Persistence Verbs](tray-persistence-verbs.md) — catalog of every tray-owned write surface (where a new racy field could be introduced)
- [Architecture](architecture.md) — Field Preservation section
