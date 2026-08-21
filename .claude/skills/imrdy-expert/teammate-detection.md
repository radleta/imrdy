---
tags: [imrdy-expert/teammates]
summary: "3-layer lead-readiness gating — subagent events never move lead status (deterministic gate), only refresh last_teammate_at (liveness tracking), which DisplayStatus.Resolve uses to render idle-with-agents-running as teal (display resolution)"
---

# Teammate Detection

imrdy's stored status answers exactly one question: **is the main session waiting for the user?**
Only the lead's own hook events may answer it. (The *displayed* color can still differ — see
[Layer 3](#layer-3--display-resolution-displaystatus-render-time-only) below.)

Claude Code sends `agent_id` and `agent_type` on hook events that fire inside a subagent. That field
is the gate.

## The Rule

| Event has `agent_id` | Effect on lead status | Effect on `last_teammate_at` |
|---|---|---|
| No (lead) | Full status derivation | untouched |
| Yes (subagent) | **None** — except clearing a resolved `permission` | refreshed |

Subagent activity carries no information about lead readiness. Modern Claude Code runs background
agents that keep working *after* the lead has returned control to the user, so "a subagent is busy"
and "the lead is busy" are independent facts.

## Layer 1 — Deterministic Gate (HookCommand + TeammateGate)

- **`agent_id` present** → subagent event. `TeammateGate.ApplyTeammateEvent()` refreshes
  `last_teammate_at` and `timestamp`. Status, `hook_event`, and `notification_type` are preserved.
- **`agent_id` absent** → lead event. Full state file write with status derivation.

### Permission-clearing exception

When the lead sits at `permission` (purple — awaiting approval) and a subagent fires
`PostToolUse`, `PostToolUseFailure`, or `PermissionDenied`, `TeammateGate.ShouldClearPermission()`
returns true and the lead's status is cleared to the derived status. Without this the lead could
stay purple indefinitely after a subagent resolved the prompt.

### Subagent lifecycle events on the lead stream

`SubagentStart`, `SubagentStop`, `TaskCreated`, `TaskCompleted`, and `TeammateIdle` describe a
subagent, not lead readiness — but they can arrive **without** `agent_id`, because the parent
spawns and reaps the subagent. The `agent_id` gate alone therefore does not catch them.
`TeammateGate.IsSubagentLifecycleEvent()` filters them on the lead path, where `HookCommand`
carries the existing status forward instead of deriving a new one.

Edge case: a subagent hook can fire before the lead session file exists (SessionStart race).
Logged as a warning, not an error.

## Layer 2 — Liveness Tracking (StateFileModel)

`last_teammate_at` (`DateTimeOffset?`) records when any subagent was last active. It must never
influence the **stored** status. It has exactly two consumers, both presentational:

1. **Icon aging** — when the lead is blocked inside a long synchronous `Task` call it fires no
   events, so the icon would dim as if the session had gone quiet. Subagent events keep it lively.
2. **`DisplayStatus.Resolve`** — an idle lead with activity inside the last 2 minutes renders teal
   rather than green (Layer 3).

Preserved across writes via `FieldPreservation.PreserveFields()`; new value wins
(`newState.LastTeammateAt ?? existing.LastTeammateAt`).

## Layer 3 — Display Resolution (DisplayStatus, render-time only)

The stored status answers "is the lead waiting?". That alone overloads green, because a lead can be
waiting *and* about to resume itself: a background agent that finishes delivers a
`<task-notification>` as a synthetic `UserPromptSubmit`. Measured on one session, **7 of 10
`UserPromptSubmit` events were agent-driven, not human**, and 4 of 10 lead `Stop`s were followed by
such a self-resume.

`DisplayStatus.Resolve(status, lastTeammateAt, now)` therefore renders an `idle` lead as `"done"`
(teal) while subagent activity is fresh. Green then means *nothing is running*.

This is **display-only**. Writing the teal value back into `StateFileModel.Status` would make
`Resolve` stop seeing an idle lead, freezing the session at teal — see
[Status Mapping](status-mapping.md).

### The window is 2 minutes, by measurement

Over 1085 consecutive-event gaps from 45 real agents: p50 = 7.5s, p90 = 23s, p95 = 38s, p99 = 75s,
max = 153s. A working agent routinely goes quiet for a minute between tool calls, so short windows
declare "finished" while it is thinking:

| Window | Gaps that overrun it (false "agents finished") |
|---|---|
| 15s | 19.6% |
| 30s | 6.9% |
| 60s | 1.7% |
| **120s** | **0.09%** (1 gap in 1085) |

Erring long is the right asymmetry: a premature flip to green costs a false "session is free"
toast — the exact noise this exists to remove — while overrunning only delays good news.

### The flip is time-driven

No hook fires when agents merely stop arriving. `OnDrainTimerTick` recomputes `Resolve` every 100ms
and compares against `SessionEntry.LastEffectiveStatus`, driving both the icon and the dwell entry
off that transition. The drain tick is consequently the **single dwell driver** for status changes;
`HandleSessionFileChanged` no longer creates dwell entries. That is what keeps teal silent and
makes the toast fire on teal → green.

## Speeds to Green

| Scenario | Path | Time to green |
|----------|------|---------------|
| No agents running | `Stop` → dwell → idle | ~5 seconds |
| Agents were running | `Stop` → teal → agents quiet 2 min → dwell → idle | ~2 minutes |
| Backstop | `Notification/idle_prompt` | 60 seconds (teal-gated) |

Teal is silent throughout; only the green transition toasts.

## What This Replaced (and why)

Three mechanisms were removed in Aug 2026 after measurement showed they were fighting the signal
rather than refining it:

1. **`TeammateGate.ShouldPromoteToBusy`** — promoted a lead at `idle`/`start` to `busy` on any
   subagent work event. With continuous subagent churn this fired constantly and overwrote the
   lead's own "waiting for user" state within milliseconds.
2. **Consensus promotion** (`ConsensusPromoted`, `TeammateQuietThreshold` 15s) — promoted `done` →
   `idle` once teammates fell quiet. Unreachable in practice, because promotion kept forcing
   `busy` rather than `done`.
3. **idle_prompt suppression** (`TeammatePresenceTimeout` 2 min) — rewrote a genuine `idle_prompt`
   back to `done` whenever teammates were active, destroying the authoritative signal.

The observed failure: a state file reading
`hook_event: Notification`, `notification_type: idle_prompt`,
`last_message: "Claude is waiting for your input"` — and `status: busy`.

Root cause was an assumption that held under the older agent-teams model and no longer does:
*subagent activity implies the lead is working*.

## Measurement (2026-08-20)

1341 hook events across 3 concurrent heavy-subagent sessions:

| Event | Lead | Subagent |
|---|---:|---:|
| PreToolUse | 167 | 1056 |
| UserPromptSubmit | 41 | 0 |
| Stop | 40 | 0 |
| Notification (all `idle_prompt`) | 26 | 0 |
| SessionStart | 6 | 0 |
| SessionEnd | 5 | 0 |

**Every one of the 40 lead `Stop` events was followed by `Notification/idle_prompt` (26) or
`UserPromptSubmit` (14) — never by further lead work.** Lead `Stop` is therefore a reliable
"waiting for the user" signal, and `Stop → idle` is correct. See [Hook Events](hook-events.md)
for the registration gap this measurement also exposed.

## Reference: clawd-on-desk

The [clawd-on-desk](https://github.com/anthropics/clawd-on-desk) reference implementation:
- Maps Stop→"attention" (one-shot 4s animation, not persistent status)
- Tracks SubagentStart→"juggling" state
- Uses `STATE_PRIORITY` ordering and `ONESHOT_STATES` set
- Different philosophy: animation-based vs persistent icon status
