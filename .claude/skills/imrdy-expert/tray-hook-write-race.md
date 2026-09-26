---
tags: [imrdy-expert/persistence]
summary: "Hook and tray both RMW session state files with no coordination — tray-side field changes are silently dropped if the field isn't on the FieldPreservation list"
last-verified: "2026-09-25"
---

# Tray vs Hook Write Race

The session state file at `~/.imrdy/sessions/{session_id}.json` has **two independent writers**:

- **Hook process** (`HookCommand`) — fires on every Claude Code hook event (potentially hundreds per session). Full state file rewrite each time.
- **Tray process** (`TrayApp.PersistSessionField`) — fires on user actions (per-session sound pack assignment, per-session icon style override, etc.) and on the desktop auto-assignments in `HandleSessionFileChanged`. Single-field mutation.

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

## What PreserveFields does and does not protect

[`FieldPreservation.PreserveFields`](field-preservation-catalog.md) merges `newState` with `existing` using `newState.Field ?? existing.Field`. The hook's `newState` never sets a tray-owned field such as `SoundPack`, so the merge resolves to `existing.SoundPack`. If `existing` was read **after** the tray wrote, the result is the tray's value. If it was read **before**, the result is the previous value and the tray's write is lost.

So `PreserveFields` closes the race only when the tray's write lands **outside** a hook's RMW window. For the loss to happen, a hook event must be in flight and the tray must write inside that hook's read-to-write window — roughly 50–200 ms per hook process. Tray mutations are rare, so the window is small, but the consequence (silent loss) makes it a real hazard.

`RunningTasks` is racy in one extra direction the other preserved fields are not: an empty roster (`[]`) is a real value that must overwrite, so a stale `existing` snapshot can resurrect a roster a later event had already emptied — and a tray RMW begun before an emptying hook write resurrects the prior roster the same way.

A field that is **not** on the preservation list is lost on every hook event regardless of timing. A tray feature that persists a new field through `PersistSessionField` without also adding it to `PreserveFields` has its value overwritten with `null` by the next hook event. The loss is silent: the tray write succeeds and lands on disk, and the loss happens in a different process on a later event. See [Field Preservation Catalog](field-preservation-catalog.md) for the list and the audit procedure.

## How to diagnose a suspected race-loss incident

1. **Enable Debug logging** via the dev-build marker (`~/.imrdy/.dev-build` — see [Dev Build Marker & Logging](dev-build-marker-logging.md)).
2. Look in the newest `~/.imrdy/logs/monitor_*.log` (tray) and `~/.imrdy/logs/hook__*.log` (hook).
3. For the affected session, find:
   - Tray write event: `Could not persist session field` (failure, Debug). `PersistSessionField` does not log on success.
   - Hook write event: `State file written: {Path}` (Debug).
4. Inspect the on-disk state file before and after each hook event. Any field that was set by the tray but is `null` after a subsequent hook event is a race-loss candidate.
5. Check the [Field Preservation Catalog](field-preservation-catalog.md): if the field is **not** on the list, it is structurally racy and will be lost on every hook event regardless of timing.

## Architectural framing

This is a **shared-data-source** anti-pattern: two systems treat one file as their working state with no mediator, so the race is inherent to that shape. The hook deliberately writes without the tray — it can run before any tray exists — and takes no lock, which is why the window stays open. Code that adds a tray-owned session field inherits this hazard; `PreserveFields` narrows it and nothing closes it.

## Cross-references

- [State File Write Path](state-file-write-path.md) — why the file is non-atomic in the first place
- [Field Preservation Catalog](field-preservation-catalog.md) — the symmetry contract that mitigates this race
- [Tray Persistence Verbs](tray-persistence-verbs.md) — catalog of every tray-owned write surface (where a new racy field could be introduced)
- [Architecture](architecture.md) — Field Preservation section
