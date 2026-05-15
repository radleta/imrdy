---
tags: [imrdy-expert/teammates]
summary: "4-layer teammate-aware notification system — deterministic gate, state tracking, consensus promotion, idle_prompt suppression"
---

# Teammate Detection

Claude Code agent teams send `agent_id` and `agent_type` on hook events from teammates. imrdy uses a 4-layer system to handle teams vs solo sessions differently.

## Layer 1 — Deterministic Gate (HookCommand + TeammateGate)

`agent_id` field on `HookEventModel` is the gate:
- **Present** → teammate event: update `last_teammate_at` timestamp on state file. Lead's status/hook_event is preserved — **except** when the lead is stuck at "permission" and the teammate fires a permission-resolution event (see below).
- **Absent** → lead event: full state file write with status derivation.

This prevents teammate tool use from overwriting the lead's status. A teammate doing Read/Edit doesn't flip the lead's icon to busy.

`TeammateGate` (static class in `Imrdy.Core/Hooks/`) encapsulates this logic via `ApplyTeammateEvent()` and `ShouldClearPermission()`. `HookCommand` delegates to `TeammateGate.ApplyTeammateEvent()` instead of doing inline timestamp-only writes.

### Permission-Clearing Exception (purple-sticking fix)

When the lead status is "permission" (purple icon — awaiting user approval), a teammate event can resolve the permission via:
- **PostToolUse** — permission was granted, tool ran
- **PostToolUseFailure** — tool ran but failed
- **PermissionDenied** — permission was denied

In these cases, `TeammateGate.ShouldClearPermission()` returns true. `ApplyTeammateEvent()` calls `StatusDerivation.DeriveStatus()` to get the derived status for the teammate event and updates the lead's status accordingly, clearing the purple icon. Without this, the lead could stay stuck at "permission" indefinitely after a teammate resolved it.

Edge case: teammate hook can fire before lead session exists (race condition on SessionStart). Logged as warning, not an error.

## Layer 2 — Timestamp Tracking (StateFileModel)

`last_teammate_at` (DateTimeOffset?) on the state file tracks when any teammate was last active. This replaces a sticky `has_teammates` bool — it ages out.

- **TeammatePresenceTimeout**: 2 minutes. If `last_teammate_at` is older than this, the session is treated as having no active teammates.
- Preserved across state file writes via `FieldPreservation.PreserveFields()`.
- New value takes precedence (null-coalescing: `newState.LastTeammateAt ?? existing.LastTeammateAt`).

## Layer 3 — Consensus Promotion (TrayApp drain timer)

On the 100ms drain timer tick, after dwell dispatch:

1. For each session in "done" status:
2. Skip if `ConsensusPromoted` is already true
3. Skip if `last_teammate_at` is null (no teammates — normal dwell path)
4. Skip if `now - last_teammate_at < TeammateQuietThreshold` (15s)
5. All teammates quiet → set `ConsensusPromoted = true`, fire `OnStatusChanged("idle", "done")` for dwell-gated icon + toast/sound (icon deferred to dwell fire to prevent green/red toggling during rapid lead tool calls)

`ConsensusPromoted` resets when status changes away from "done".

## Speeds to Green

| Scenario | Path | Time to green |
|----------|------|---------------|
| No teammates | Stop→done→dwell→idle | ~5 seconds |
| No teammates (backstop) | idle_prompt Notification | 60 seconds |
| Teammates finish | Stop→done→consensus→idle | ~15 seconds |
| Teammates age out | Teammate presence expires (2 min) → dwell | ~2 minutes |

For team sessions, idle_prompt is suppressed (see Layer 4 below), so consensus is the primary path. The 60s backstop only applies to solo sessions.

## Dwell Suppression for Teams

When status is "done" AND `last_teammate_at` is within TeammatePresenceTimeout (2 min):
- Do NOT create a dwell entry (normal 5s done→idle path is suppressed)
- Consensus check handles promotion instead
- This prevents premature idle toasts while teammates are still working

## Layer 4 — idle_prompt Suppression (TrayApp)

`idle_prompt` is a 60s backstop Notification that fires even when subagents are still active. Without gating, it would bypass the teammate-aware consensus system and cause premature green/toast.

In `HandleSessionFileChanged`, when an `idle_prompt` arrives for a session with active teammates (`last_teammate_at` within 2 min):
- Status is rewritten from "idle" back to "done"
- NotificationType is cleared
- Session stays teal; no toast or sound fires
- Consensus (Layer 3) remains the path to green

This closes the gap where idle_prompt was the only notification path not respecting the teammate gate.

## Discovered Team Lifecycle Events

These events are registered but not yet observed in the wild (as of 2026-04-14):
- **TaskCreated** — fires when lead creates a task for a teammate
- **TaskCompleted** — fires when a delegated task completes
- **TeammateIdle** — fires when a specific teammate goes idle (carries agent_id)

These provide deterministic lifecycle signals. Currently mapped to "busy" — may be refined after observing real data.

## Reference: clawd-on-desk

The [clawd-on-desk](https://github.com/anthropics/clawd-on-desk) reference implementation:
- Maps Stop→"attention" (one-shot 4s animation, not persistent status)
- Tracks SubagentStart→"juggling" state
- Uses `STATE_PRIORITY` ordering and `ONESHOT_STATES` set
- Different philosophy: animation-based vs persistent icon status
