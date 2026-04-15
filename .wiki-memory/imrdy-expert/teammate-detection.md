---
tags: [imrdy/teammates]
updated: 2026-04-14
summary: "3-layer teammate-aware notification system — deterministic gate, state tracking, consensus promotion"
---

# Teammate Detection

Claude Code agent teams send `agent_id` and `agent_type` on hook events from teammates. imrdy uses a 3-layer system to handle teams vs solo sessions differently.

## Layer 1 — Deterministic Gate (HookCommand)

`agent_id` field on `HookEventModel` is the gate:
- **Present** → teammate event: only update `last_teammate_at` timestamp on state file. Do NOT change lead's status/hook_event.
- **Absent** → lead event: full state file write with status derivation.

This prevents teammate tool use from overwriting the lead's status. A teammate doing Read/Edit doesn't flip the lead's icon to busy.

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
5. All teammates quiet → set `ConsensusPromoted = true`, fire `OnStatusChanged("idle", "done")`

`ConsensusPromoted` resets when status changes away from "done".

## Three Speeds to Green

| Scenario | Path | Time to green |
|----------|------|---------------|
| No teammates | Stop→done→dwell→idle | ~5 seconds |
| Teammates finish | Stop→done→consensus→idle | ~15 seconds |
| Backstop | idle_prompt Notification | 60 seconds |

## Dwell Suppression for Teams

When status is "done" AND `last_teammate_at` is within TeammatePresenceTimeout (2 min):
- Do NOT create a dwell entry (normal 5s done→idle path is suppressed)
- Consensus check handles promotion instead
- This prevents premature idle toasts while teammates are still working

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
