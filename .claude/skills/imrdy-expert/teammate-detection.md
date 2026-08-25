---
tags: [imrdy-expert/teammates]
summary: "How imrdy reads the background_tasks roster Claude Code sends: the agent_id gate keeps subagents from moving lead status, Stop/SubagentStop supply running_tasks, and DisplayStatus.Resolve renders an idle lead with a non-empty roster as teal"
---

# Teammate Detection

imrdy's stored status answers exactly one question: **is the main session waiting for the user?**
Only the lead's own hook events may answer it. (The *displayed* colour can still differ — see
[Display resolution](#display-resolution-render-time-only) below.)

Two independent facts drive the tray, and they come from two different places:

| Fact | Source | Stored as |
|---|---|---|
| Is the lead waiting for the user? | the lead's own hook events (no `agent_id`) | `StateFileModel.Status` |
| Is anything still running? | the `background_tasks` roster Claude Code sends | `StateFileModel.RunningTasks` (`running_tasks`) |

imrdy does not *infer* the second fact from the first, and it does not infer it from silence.
Claude Code reports it directly, and imrdy stores what it was told.

## The `agent_id` gate

Claude Code sends `agent_id` and `agent_type` on hook events that fire inside a subagent. That
field is the gate.

| Event has `agent_id` | Effect on lead status | Effect on the stored roster |
|---|---|---|
| No (lead) | Full status derivation | overwritten when the payload carries `background_tasks`, otherwise preserved (or cleared — see [`ClearsRoster`](#the-roster-clearing-rule-d25)) |
| Yes (subagent) | **None** — except clearing a resolved `permission` | overwritten when the payload carries `background_tasks`, otherwise preserved |

Subagent activity carries no information about lead readiness. Modern Claude Code runs background
agents that keep working *after* the lead has returned control to the user, so "a subagent is busy"
and "the lead is busy" are independent facts.

`HookCommand` routes on `agent_id`:

- **`agent_id` present** → subagent event. `TeammateGate.ApplyTeammateEvent()` refreshes
  `timestamp` and applies the roster. Status, `hook_event`, and `notification_type` are preserved.
- **`agent_id` absent** → lead event. Full state file write with status derivation.

The roster is extracted **once, before that branch**, because `SubagentStop` normally carries
`agent_id` and takes the teammate path but can also reach the lead path without one (the parent
spawns and reaps the subagent). A roster applied on only one branch would be silently dropped on
the other.

### Permission-clearing exception

When the lead sits at `permission` (purple — awaiting approval) and a subagent fires
`PostToolUse`, `PostToolUseFailure`, or `PermissionDenied`, `TeammateGate.ShouldClearPermission()`
returns true and the lead's status is cleared to the derived status. Without this the lead could
stay purple indefinitely after a subagent resolved the prompt. This behaviour does not vary with
the roster — the two are orthogonal.

### Subagent lifecycle events on the lead stream

`SubagentStart`, `SubagentStop`, `TaskCreated`, `TaskCompleted`, and `TeammateIdle` describe a
subagent, not lead readiness — but they can arrive **without** `agent_id`, because the parent
spawns and reaps the subagent. The `agent_id` gate alone therefore does not catch them.
`TeammateGate.IsSubagentLifecycleEvent()` filters them on the lead path, where `HookCommand`
carries the existing status forward instead of deriving a new one.

Edge case: a subagent hook can fire before the lead session file exists (SessionStart race).
Logged as a warning, not an error.

## The roster (`background_tasks` → `running_tasks`)

`Stop` and `SubagentStop` payloads carry a top-level `background_tasks` array listing everything
still running for the session. **Only those two events carry it** — across the whole of
`evidence/capture.log`, 13/13 `Stop` and 96/96 `SubagentStop` payloads have the key, and no other
event type does. `HookCommand` deserializes it into `List<BackgroundTaskModel>?` and persists it as
`StateFileModel.RunningTasks` (`running_tasks` on disk). See
[Hook Events](hook-events.md#the-running-work-roster) for the per-entry wire shape.

**`null` and `[]` are different facts and are never normalised into each other.**

| Stored value | Means | Displayed when lead is `idle` |
|---|---|---|
| `null` | no measurement — nothing has ever reported a roster for this session | green (D6 degradation path) |
| `[]` | measured: nothing is running | green |
| 1+ entries | measured: work is still running | teal |

`FieldPreservation.PreserveFields()` carries the roster across writes with the standard
`newState.RunningTasks ?? existing.RunningTasks` merge — a write that says nothing about running
work leaves the previous measurement in place.

**Every entry counts, regardless of its `status` value (D19).** All 277 roster entries across
`evidence/capture.log` are `status: "running"`, so a filter on that value would be written against
a vocabulary with exactly one observed member and would fail silently the day the vocabulary
changed. Counting everything errs toward teal (silent); filtering would err toward premature green
(noisy). The entry `status` rides on the `tasks=` token in the hook log so drift is detected
empirically rather than guessed at.

**The roster is trusted verbatim — there is no self-inclusion filter (D3).** A `SubagentStop` can
list its own `agent_id` among the running entries, but every observed case self-corrected within
one event. See
[SubagentStop rosters usually name a sibling](subagentstop-roster-usually-names-a-sibling.md)
before concluding that a non-empty roster on a `SubagentStop` *is* self-inclusion — usually it is
a stopping agent correctly reporting a different agent that is still running.

## The roster-clearing rule (`ClearsRoster`, D25)

Preserving the previous roster is the default, and it is correct almost everywhere. Two events are
the exception and clear the stored roster to `[]` instead — **for different reasons**:

- **`Stop`** — the lead reported no running work. When a `Stop` arrives with the field absent
  entirely, `ClearsRoster` degrades it to an empty roster rather than preserving the old one (D6).
  Preserving on the very event that *establishes* idle would strand a session at teal with nothing
  left to clear it.
- **`SessionStart` with `source` `startup` or `resume`** — a **process boundary**.
  `background_tasks` are owned by the Claude Code process that spawned them, so a roster left by a
  previous process describes work that is already dead. With `source` `clear` or `compact` the
  process is still alive and its work keeps running, so the roster is **preserved**.

```csharp
private static bool ClearsRoster(string eventName, string? source) =>
    string.Equals(eventName, "Stop", StringComparison.OrdinalIgnoreCase)
    || (string.Equals(eventName, "SessionStart", StringComparison.OrdinalIgnoreCase)
        && (string.Equals(source, "startup", StringComparison.OrdinalIgnoreCase)
            || string.Equals(source, "resume", StringComparison.OrdinalIgnoreCase)));
```

Four properties of that helper are load-bearing. Each is a trap that gets re-introduced by an
edit that looks like a simplification:

1. **The event matches are exact equalities, never substrings.** `SubagentStop` ends with `Stop`
   and `SubagentStart` ends with `Start`. Any `StartsWith`/`EndsWith`/`Contains` predicate would
   fire on those two events and wipe a live roster — on `SubagentStop`, the single event most
   likely to be carrying one.

2. **The `source` filter is an allowlist, not a denylist.** It names `{startup, resume}` and
   preserves everything else, so an **unknown future `source` value preserves**. That direction is
   deliberate: preserving wrongly leaves a session at stale teal, which is silent, while clearing
   wrongly produces false green, which fires a toast at the user about work that is still running.
   A `{clear, compact}` denylist would invert that asymmetry. This is the same choice D3 and D19
   make elsewhere — when in doubt, fail quiet.

3. **`PostCompact`, `PermissionDenied`, and `Notification`/`idle_prompt` also reach `idle` without
   carrying a roster, and preserving there is correct.** All three are intra-session: compaction is
   a context operation the process survives, denying one permission ends a turn without killing
   running work, and `idle_prompt` fires seconds after the `Stop` that wrote the roster in the
   first place. Clearing on any of them would flip teal → green immediately after every `Stop` that
   reported running work — precisely the false-green bug this mechanism exists to remove.

4. **There is no empirical basis for any `SessionStart` frequency claim.** The event appears
   **0 times** in `evidence/capture.log`, which begins mid-session. This rule is derived from
   process semantics — who owns a background task's lifetime — and not from a measurement. Do not
   attach an observed count to it, and treat any figure you find quoted against it as invented.

## Display resolution (`DisplayStatus`, render-time only)

The stored status answers "is the lead waiting?". That alone overloads green, because a lead can be
waiting *and* about to resume itself: a background agent that finishes delivers a
`<task-notification>` as a synthetic `UserPromptSubmit`. Measured on one session, **7 of 10
`UserPromptSubmit` events were agent-driven, not human**, and 4 of 10 lead `Stop`s were followed by
such a self-resume.

`DisplayStatus.Resolve(status, runningTasks)` therefore renders an `idle` lead as `"done"` (teal)
whenever the stored roster is non-empty. Green then means *nothing is running* — not "nothing
observed recently".

```csharp
public static string Resolve(string status, IReadOnlyList<BackgroundTaskModel>? runningTasks)
    => string.Equals(status, "idle", StringComparison.OrdinalIgnoreCase) && runningTasks is { Count: > 0 }
        ? "done"
        : status;
```

Only the `idle` case is rewritten; every other status already describes the lead accurately and
passes through untouched. See [Status Mapping](status-mapping.md) for the full matrix.

This is **display-only**. Writing the teal value back into `StateFileModel.Status` would make
`Resolve` stop seeing an idle lead, freezing the session at teal.

### Why the mechanism needs no timer, no expiry, and no cleanup policy

The whole design rests on one invariant:

> Whenever the lead is `idle`, the stored roster describes work owned by the **currently-running**
> Claude Code process.

It holds because of three things, all of them already covered above:

- `Stop` lands `Status = "idle"` and `RunningTasks = roster` in the **same atomic state-file
  write**, so the tray can never observe one without the other.
- While the lead is not `idle`, the roster is ignored, so a stale one is unreachable while it
  would matter.
- The only staleness a process could inherit is its predecessor's, and `ClearsRoster` severs
  exactly that inheritance at the `SessionStart` process boundary.

There is nothing left for a timer to expire, which is why no expiry or cleanup policy appears
anywhere on this path.

### The teal → green flip is hook-announced

An emptied roster arrives on a hook event, so the transition is announced rather than detected by
elapsed time. `OnDrainTimerTick` still recomputes `Resolve` every 100ms and compares against
`SessionEntry.LastEffectiveStatus`, driving both the icon and the dwell entry off that transition
(D7 keeps this loop unchanged). The drain tick is consequently the **single dwell driver** for
status changes; `HandleSessionFileChanged` no longer creates dwell entries. That is what keeps teal
silent and makes the toast fire on teal → green. See
[Notification Dwell](notification-dwell.md).

## Speeds to Green

| Scenario | Path | Time to green |
|----------|------|---------------|
| No work running | `Stop` with empty roster → dwell → idle | ~5 seconds |
| Work was running | `Stop` with entries → teal → next roster-bearing event reports `[]` → dwell → idle | as soon as the roster empties |
| Backstop | `Notification/idle_prompt` | 60 seconds (teal-gated by the preserved roster) |

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
3. **idle_prompt suppression** — rewrote a genuine `idle_prompt` back to `done` whenever teammates
   were active, destroying the authoritative signal.

The observed failure: a state file reading
`hook_event: Notification`, `notification_type: idle_prompt`,
`last_message: "Claude is waiting for your input"` — and `status: busy`.

Root cause was an assumption that held under the older agent-teams model and no longer does:
*subagent activity implies the lead is working*.

A **fourth** mechanism was removed in Aug 2026 for a different reason. Liveness was previously
inferred from a per-session timestamp of the last subagent event plus a presence window: an idle
lead whose most recent subagent activity was recent enough displayed teal, and green arrived once
the window elapsed with no new events. That was an inference from silence, and silence is
ambiguous — a working agent routinely goes quiet between tool calls, so the window had to be tuned
to trade premature green against delayed green, and no setting removed the trade-off. The roster
replaces the inference with a report: Claude Code says what is running, imrdy stores it, and the
tray reads it. There is no window to tune because there is nothing being inferred.

## Measurement (2026-08-20)

1341 hook events across 3 concurrent heavy-subagent sessions. This corpus establishes the
lead-vs-subagent split and the `Stop → idle` mapping; it predates the roster work and says nothing
about `ClearsRoster` (see sub-point 4 above):

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
