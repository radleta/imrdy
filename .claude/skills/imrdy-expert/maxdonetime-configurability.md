---
tags: [imrdy/configuration, imrdy/teaming]
updated: 2026-04-30
summary: "MaxDoneTime consensus delay remains constant pending user reports of false promotions"
---

## MaxDoneTime Configuration Strategy

`MaxDoneTime` (90s) is currently a private constant in `TrayApp` rather than a user-configurable field. This is a deliberate tradeoff:

### Why 90s Was Chosen
- Exceeds observed teammate-sequence latency (~30s) with 3× safety margin
- No user has yet reported the "stuck teal" bug (session remains at `done` when teammates are still working)
- Calibration requires real-world data; shipping a config field without observed need creates a poorly-anchored default range

### When to Promote to Config
Promote `MaxDoneTime` to a configurable field **when one of these conditions is met:**

1. Users report false promotions — teammates performing legitimately long cleanup operations at `done` status before the lead re-fires, causing unwanted `done→idle` transitions
2. The 90s threshold proves too short for an observed class of teammate patterns (e.g., slow CI pipelines, long-running post-tool tasks)

### Implementation Path
Add a `teaming.maxDoneTimeSeconds` integer field in `ImrdyConfig` (`src/Imrdy.Core/ImrdyConfig.cs`).

Follow the existing pattern used for:
- `teaming.teammatePresenceTimeoutSeconds` (2 min default)
- `teaming.teammateQuietThresholdSeconds` (15s default)

Search `TeammatePresenceTimeout` in `TrayApp.cs` to find the corresponding read sites.

**Suggested configurable range:** 30s–300s with 90s as the default.

---

## Related Pages

- [WSL→Windows PATH Passthrough Baseline](wsl-interop-baseline.md)
