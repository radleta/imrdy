---
tags: [imrdy/notifications]
updated: 2026-04-14
summary: "Dwell timer system that gates toast/sound behind status settling — prevents notification storms"
---

# Notification Dwell

`NotificationDwellState` in `Imrdy.Core/Sound/` gates toast and sound notifications behind per-status dwell timers. Icon updates remain immediate — only the interrupt layer (toast + sound) is gated.

## How It Works

1. Status change fires → icon updates immediately
2. `OnStatusChanged()` creates/replaces a pending dwell entry
3. On each 100ms drain timer tick, `GetFiredSessions()` checks:
   - Has the dwell duration elapsed? (status-dependent)
   - Has the 10s per-session toast cooldown passed?
4. If both pass → fire notification (toast + sound)

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

## Teammate-Aware Suppression

See [Teammate Detection](teammate-detection.md) for the full 4-layer system. Key interactions with dwell:

- **Dwell suppression**: When status is "done" and teammates are active, no dwell entry is created. Consensus handles promotion instead.
- **idle_prompt suppression**: The 60s `idle_prompt` backstop is rewritten from "idle" back to "done" when teammates are active, preventing it from bypassing the consensus gate.

## Sound Triggers

Sound dispatch uses the `FiredNotification` record which carries `PreviousStatus` for transition-aware dispatch:
- done→idle: plays "Finished" sound (session completed)
- Other transitions: match on current status

## Toast Events

`BalloonTipManager.DefaultToastEvents`: idle, attention, permission, error. "done" is intentionally NOT in the set — no toast on done (it's an intermediate status). Toast fires when done settles to idle via dwell or consensus.
