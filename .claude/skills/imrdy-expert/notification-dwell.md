---
tags: [imrdy-expert/notifications]
summary: "Dwell timer system that gates toast/sound behind status settling — prevents notification storms"
---

# Notification Dwell

`NotificationDwellState` in `Imrdy.Core/Sound/` gates toast and sound notifications behind per-status dwell timers. Icon updates remain immediate — only the interrupt layer (toast + sound) is gated.

## How It Works

1. On each 100ms drain tick, `OnDrainTimerTick` recomputes `DisplayStatus.Resolve` per session and
   diffs it against `SessionEntry.LastEffectiveStatus` — this **effective-status transition**, not
   the raw hook-driven `StateFileModel.Status` write, is what feeds the dwell system. It is the sole
   dwell driver for status changes; `HandleSessionFileChanged` no longer creates a dwell entry on
   `statusChanged`. Icon updates track the same effective status and land within one 100ms tick.
2. On a transition, `OnStatusChanged()` creates/replaces a pending dwell entry for the new effective
   status (this happens for every status, including "done" — see below).
3. On each 100ms drain timer tick, `GetFiredSessions()` checks:
   - Has the dwell duration elapsed? (status-dependent; "done" has no explicit entry and falls back
     to the 3s default)
   - Has the 10s per-session toast cooldown passed?
4. If both pass → the entry fires and is dispatched to the toast/sound layer, which independently
   decides whether "done" is worth surfacing (see below).

## Dwell Durations

| Status | Duration | Rationale |
|--------|----------|-----------|
| idle | 5s | Prevents false idle during rapid turn-taking |
| compact | 5s | Compaction is brief, don't interrupt |
| busy | 3s | Quick transitions settle fast |
| error | 3s | Errors need attention but may auto-resolve |
| permission | 3s | Prompt may be handled quickly |
| attention | 3s | Notifications may cluster |
| end | 2s | Session end is final |

## Defense in Depth

Three layers prevent notification storms:
1. **Dwell timer** — status must settle for N seconds
2. **Toast cooldown** — 10s per-session minimum between toasts
3. **CooldownTracker** — 5s per-session sound cooldown (separate from dwell)

## Latest-Wins Replacement

When a new status change arrives before the dwell fires, it *replaces* the pending entry (latest wins). `LastNotifiedAt` is intentionally preserved across replacements to maintain the toast cooldown. This means rapid status cycling never triggers — only the final settled status fires.

## Teammate-Aware Behavior (rewritten Aug 2026)

See [Teammate Detection](teammate-detection.md) for the full lead-readiness/liveness/display-resolution
system this replaced. Dwell no longer suppresses anything based on teammate activity — it dwells
every effective-status transition, including "done", and lets the toast/sound layer decide what's
worth surfacing:

- **"done" dwells silently by construction, not by suppression.** A busy→done (or idle→done)
  transition creates and fires a dwell entry like any other, but `BalloonTipManager.DefaultToastEvents`
  doesn't include "done" (no toast) and `TriggerStatusChangeSound`'s switch has no `(_, "done")` arm
  (no sound). Only the **teal → green edge** (done→idle) is audible, via
  `(_, "idle") when previousStatus is "busy" or "done"` → `SoundEvent.Finished`.
- **`idle_prompt` has its own, separate dwell entry**, keyed by `NotificationType` rather than by the
  effective-status loop. `HandleSessionFileChanged` maps it through
  `DisplayStatus.Resolve("idle", state.LastTeammateAt, now)`: if that resolves to "done" (subagents
  still fresh), **no dwell entry is created for the notification at all** — the 60s idle_prompt
  backstop stays silent while agents run, without touching `StateFileModel.Status`. If it resolves to
  "idle", it dwells and, on firing, toasts (idle_prompt is exempted from the "notification-type
  required" check) and plays `SoundEvent.Forgotten`.

## Sound Triggers

Sound dispatch uses the `FiredNotification` record which carries `PreviousStatus` for transition-aware dispatch:
- done→idle: plays "Finished" sound (session completed)
- Other transitions: match on current status

## Toast Events

`BalloonTipManager.DefaultToastEvents`: idle, attention, permission, error. "done" is intentionally NOT in the set — no toast on done (it's an intermediate status). Toast fires on the done→idle edge, once the effective-status loop detects it and dwell settles.
