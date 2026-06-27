---
tags: [imrdy-expert/persistence]
summary: "The 6 sticky fields in FieldPreservation.PreserveFields, the merge pattern, and the symmetry contract every new tray-owned field must satisfy"
---

# Field Preservation Catalog

`FieldPreservation.PreserveFields(newState, existing)` (`src/Imrdy.Core/Hooks/FieldPreservation.cs`) is the **symmetry contract** between hook writes and tray writes on session state files. It is the only mechanism preventing tray-side field changes from being clobbered by the next hook event.

## The catalog (authoritative)

As of `develop` branch, the merge preserves exactly these fields:

```csharp
return newState with
{
    SoundPack       = newState.SoundPack       ?? existing.SoundPack,
    DesktopIndex    = newState.DesktopIndex    ?? existing.DesktopIndex,
    IconStyle       = newState.IconStyle       ?? existing.IconStyle,
    LastTeammateAt  = newState.LastTeammateAt  ?? existing.LastTeammateAt,
    StartedAt       = newState.StartedAt       ?? existing.StartedAt,
    WslDistro       = newState.WslDistro       ?? existing.WslDistro,
};
```

| Field | Owner | Why it's preserved |
|---|---|---|
| `SoundPack` | Tray (per-session override) | Hook never sets it; without preservation, every hook event would `null` it out |
| `DesktopIndex` | Tray (assigned when session first seen) | Hook never sets it; tracks which virtual desktop the session lives on |
| `IconStyle` | Tray (per-session override) | Hook never sets it; resolution chain: session → workspace → global |
| `LastTeammateAt` | Hook (teammate events) | Set on teammate hook events only; preserved across lead hook events |
| `StartedAt` | Hook (first SessionStart only) | Set once; never overwritten on subsequent SessionStart (reconnect / tray restart) |
| `WslDistro` | Hook (from env var, falls back to existing) | Stable per session; falls back to preserved value when env var not available |

> **Authoritative source:** when in doubt, `FieldPreservation.PreserveFields` is the canonical list. Doc pages mirror it; the code wins on disagreements.

## The merge pattern

```csharp
newField ?? existingField
```

- **New value wins if it is non-null.** A hook event that explicitly sets a preserved field overwrites the existing value.
- **Existing value wins if the new value is null.** This is the common case for tray-owned fields — the hook never sets them, so they always fall through to `existing`.

This is **not a deep merge**. Nested object fields like `Hook` accumulator data are not selectively preserved — they're replaced wholesale by the hook's `newState`. Only the six scalar fields above survive.

## The symmetry contract

> **Every tray-written field on `StateFileModel` MUST appear in `PreserveFields`. Every field in `PreserveFields` MUST correspond to a real writer (tray-only or hook-conditional).**

Why: see [Tray vs Hook Write Race](tray-hook-write-race.md). The hook process writes the full state file on every event. If the hook's `newState` does not carry a field, and the field is not on the preservation list, that field is written as `null` — silently dropping whatever the tray (or a prior hook event) wrote.

This contract has no compile-time enforcement. Adding a new tray-persisted field is a **three-touch change**:

1. Add the field to `StateFileModel` (record property).
2. Add a `PersistSessionField` wrapper or extend an existing one in `TrayApp`.
3. **Add the field to `FieldPreservation.PreserveFields`.** (This is the step that's easy to forget.)

A missed step 3 produces a silent-loss bug: the tray's write succeeds and the value lands on disk; the next hook event silently overwrites it with `null`.

## How to audit the catalog

When in doubt about whether a field is racy:

1. Find the field on `StateFileModel` (`src/Imrdy.Core/State/StateFileModel.cs`).
2. Search the codebase for writers: `grep "FieldName = " src/`.
   - If the only writer is `HookCommand` building `newState`, no preservation needed.
   - If any writer is `TrayApp.PersistSessionField` or another tray-side path, the field MUST be in `PreserveFields`.
3. Verify by reading `FieldPreservation.cs`. If the field is missing, this is a latent persistence-loss bug.

This audit catches the drift hazard before it produces user-visible incidents.

## Alternative encodings — why we use `?? existing`

The `?? existing` pattern requires the preserved fields to be nullable on `StateFileModel`. This is intentional:

- A non-nullable field (e.g., `string Status`) cannot distinguish "hook set it" from "hook didn't set it" — the value is always present.
- Nullable fields encode the "not set by this writer" state explicitly, which is exactly what the merge needs.

Three-state nullable fields elsewhere in the codebase (`DiagnosticsConfig.IpcEnabled: bool?`) use the same encoding for a different reason — to distinguish "default" from "explicit false." That pattern is unrelated to this race; do not conflate them.

## Cross-references

- [State File Write Path](state-file-write-path.md) — why session state is non-atomic, enabling the race in the first place
- [Tray vs Hook Write Race](tray-hook-write-race.md) — the structural race this catalog mitigates
- [Tray Persistence Verbs](tray-persistence-verbs.md) — every tray-side write path that depends on this contract
- [Architecture](architecture.md) — Field Preservation section
