---
tags: [imrdy-expert/status]
summary: "Two-layer status mapping: hook event → base status → RGB color, with 9 base statuses"
---

# Status Mapping

imrdy uses a two-layer status mapping: hook events derive a status string, which maps to a base status, which maps to an RGB color.

See [Hook Events](hook-events.md) for the full list of events that produce these statuses.

## Layer 1: Event → Status (StatusDerivation)

`StatusDerivation.DeriveStatus()` maps hook event names to status strings. Uses a static dictionary with `StringComparer.OrdinalIgnoreCase`. Special cases handled before dictionary lookup:
- SessionStart + source="resume" → idle
- Notification + notification_type="permission_prompt" → permission
- Notification + notification_type="idle_prompt" → idle

Unknown events return "unknown".

## Layer 2: Status → Base Status (StatusMap)

`StatusMap.ResolveBaseStatus()` maps hook statuses to base statuses:
- "start" → "idle" (new session starts as idle)
- "end" → "unknown" (session terminated)
- All others pass through as-is

## Layer 3: Base Status → Color (StatusMap)

| Base Status | RGB | Visual | Meaning |
|-------------|-----|--------|---------|
| busy | (230, 40, 40) | Red | Claude is working |
| done | (40, 180, 170) | Teal | **Display-only** — lead is idle but subagents are still running |
| idle | (40, 200, 40) | Green | Genuinely waiting for user |
| attention | (255, 120, 0) | Orange | Notification needs attention |
| error | (230, 200, 40) | Yellow | Tool or stop failure |
| permission | (180, 60, 230) | Purple | Waiting for user approval |
| compact | (60, 120, 230) | Blue | Context compaction in progress |
| unknown | (128, 128, 128) | Gray | Unknown/terminated |
| workspace | (255, 255, 255) | White | Controller tray icon |

## The "done" Status — idle, but not free

**No hook event derives "done".** It is produced entirely by `DisplayStatus.Resolve` at render time:
an `idle` lead whose `last_teammate_at` is within 2 minutes displays as teal instead of green.

The distinction it draws is between two things green used to conflate:

| | Lead waiting? | Agents running? | Colour |
|---|---|---|---|
| Working | no | — | red / busy |
| **Idle, agents running** | yes | yes | **teal / done** |
| Free | yes | no | green / idle |

Teal exists because an idle lead with background agents may **resume itself** without the user —
a completing background agent delivers a `<task-notification>` as a synthetic `UserPromptSubmit`.
Measured on one session: 7 of 10 `UserPromptSubmit` events were agent-driven, not human. Green
should mean "nothing is running and this is yours", so that case gets its own colour.

- Icon: teal (distinct from green/idle)
- **Silent** — "done" is not in `DefaultToastEvents` and has no sound mapping
- The toast + `SoundEvent.Finished` fire on the **teal → green** edge, via the existing
  `(_, "idle") when previousStatus is "busy" or "done"` sound rule

### Why it must stay display-only

`StateFileModel.Status` records lead readiness and nothing else. If the teal value were ever
written back into it, `Resolve` would stop seeing an `idle` lead and the session would be stuck at
teal permanently. The dwell-fired handler in `TrayApp` used to write the settled status back into
`entry.State`; that write-back was removed for exactly this reason. Icons read
`SessionEntry.EffectiveStatus`; `State.Status` stays the lead's truth.

### The teal → green flip is time-driven

Nothing announces it — no hook fires when agents merely stop arriving. `OnDrainTimerTick` compares
`DisplayStatus.Resolve(...)` against `SessionEntry.LastEffectiveStatus` every 100ms and drives both
the icon and the dwell entry from that transition. This makes the drain tick the single dwell
driver for status changes, which is what keeps teal silent and green audible.

## Aging

Icons dim over time based on `LastSeenAt` (last user interaction):
- Tier 0 (< 1 min): 100% brightness
- Tier 1 (1-3 min): 85%
- Tier 2 (3-7 min): 70%
- Tier 3 (7-15 min): 55%
- Tier 4 (15+ min): 40%
