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
| done | (40, 180, 170) | Teal | Turn finished, may resume |
| idle | (40, 200, 40) | Green | Genuinely waiting for user |
| attention | (255, 120, 0) | Orange | Notification needs attention |
| error | (230, 200, 40) | Yellow | Tool or stop failure |
| permission | (180, 60, 230) | Purple | Waiting for user approval |
| compact | (60, 120, 230) | Blue | Context compaction in progress |
| unknown | (128, 128, 128) | Gray | Unknown/terminated |
| workspace | (255, 255, 255) | White | Controller tray icon |

## The "done" Status

"done" (teal) was introduced to solve false idle toasts during teams. Stop events fire between turns — they don't mean idle. "done" is a visible intermediate that means "turn finished" without implying "waiting for user."

- Icon: teal (distinct from green/idle)
- No toast fires on "done" (not in DefaultToastEvents)
- Promotes to "idle" (green) via: dwell (5s, solo) or consensus (15s, teams). idle_prompt (60s) is the backstop for solo sessions; suppressed for teams when teammates are active (see [Teammate Detection](teammate-detection.md) Layer 4)

## Aging

Icons dim over time based on `LastSeenAt` (last user interaction):
- Tier 0 (< 1 min): 100% brightness
- Tier 1 (1-3 min): 85%
- Tier 2 (3-7 min): 70%
- Tier 3 (7-15 min): 55%
- Tier 4 (15+ min): 40%
