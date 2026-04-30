---
tags: [imrdy/notifications, imrdy/architecture]
summary: "State-matrix audit fixes: 9 bugs across notification dwell, consensus promotion, and overlay interaction."
---

# State-Matrix Fixes

Record of the 9 bugs fixed during the state-matrix audit branch, architectural knowledge updates discovered, and wiki corrections applied.

## Bugs Fixed (File:Line Citations)

| # | Component | Bug | Fix |
|---|-----------|-----|-----|
| 1 | Consensus | Stuck-at-done when teammates pulse faster than 15s quiet threshold | `ConsensusGate.IsEligibleForPromotion` + `MaxDoneTime` (90s) bypass via `entry.StatusSince` |
| 2 | Dwell (Solo) | Solo `done → idle` promotion fired `done` status instead of `idle` to downstream | `TrayApp.cs:~575-600` — rewrite `state.Status = "idle"` before `OnStatusChanged` |
| 3 | Overlay | Gap click in overlay did not fire `SurfaceInteracted` → dashboard re-showed on next tick | `InteractiveOverlayWindow.cs:~87` — fire `SurfaceInteracted` on any mouse-down, not just icon-hit |
| 4 | Dashboard | Hover form race on virtual desktop switch — session ID could change between dwell-trigger and form update | `HoverDashboardController.cs:~547` — add session-ID guard (early-return if `sessionId` no longer matches) |
| 5 | Status | `Notification + elicitation_dialog` mapped to `attention` (orange) in icon layer, but `permission` (purple) in dwell/sound layer | `StatusDerivation.cs:55-61` — add special-case if-block for `Notification + elicitation_dialog → permission` |
| 6 | Teammate | First teammate event lost if lead session state file did not exist yet | `HookCommand.cs:131-136` — synthesize minimal state file with `LastTeammateAt = now` when lead absent |
| 7 | IPC | Empty `verb` field in error responses (body not yet deserialized at rejection point) | `InspectIpcServer.cs:~134` — add code comment; new test file `InspectIpcServerTests.cs` covers all 3 error cases |
| 8 | IPC | `TaskCompletionSource` left orphaned when 2s `WaitAsync` timeout fires | `InspectIpcServer.cs:~178-200` — add `.ContinueWith` observer or `try/catch` to absorb late-arriving exceptions |
| 9 | IPC | IOException when no IPC server listening (REFUTED by runtime probe) | 09a confirmed `IOException` does NOT surface on .NET 10 → 09b skipped per D6 |

## Architectural Knowledge Updates

### Three Constants Control Different Promotion/Suppression Behaviors

Critical distinction to prevent future bugs (easy to conflate):

| Constant | Value | Role | Location |
|---|---|---|---|
| `TeammatePresenceTimeout` | 2 min | Gates `hasActiveTeammates` flag (suppression behaviors only) | `TeammateGate.cs` + `TrayApp.cs` consumers |
| `TeammateQuietThreshold` | 15s | Consensus quiet-path: promote when no teammate activity for 15s | `TrayApp.cs:421` → `ConsensusGate.IsEligibleForPromotion` |
| `MaxDoneTime` | 90s | Bypass: promote regardless of teammate activity after 90s at "done" status | `TrayApp.cs:48` + `ConsensusGate.IsEligibleForPromotion` |

**Key insight:** `TeammatePresenceTimeout` has NO promotion role — it is a suppression gate only. Promotion is handled exclusively by `ConsensusGate.IsEligibleForPromotion` via the latter two constants.

### `entry.StatusSince` Anchor for Promotion Eligibility

`StatusSince` is a `DateTimeOffset` set at `TrayApp.cs:570` on every status transition. Serves as the time-in-status anchor for the `MaxDoneTime` (90s) bypass check in `ConsensusGate.IsEligibleForPromotion`.

2×2 promotion matrix:
- **LastTeammateAt = null, StatusSince < 90s** → blocked (no teammates, not aged)
- **LastTeammateAt = null, StatusSince ≥ 90s** → promote (no teammates, aged out)
- **LastTeammateAt ≠ null (active), StatusSince < 90s** → blocked (teammates present, not aged)
- **LastTeammateAt ≠ null (active), StatusSince ≥ 90s** → promote (teammates present but aged past 90s)

### Elicitation Dialog Handled at Two Layers

The `Notification + elicitation_dialog → permission` mapping exists at two orthogonal layers:
- **Icon layer** (`StatusDerivation.cs:55-61`, added step 04) — drives the icon color
- **Dwell/sound layer** (`TrayApp.cs:611` + `:1872`, pre-existed step 04) — drives dwell classification and sound dispatch

Before step 04, only the dwell/sound layer handled `elicitation_dialog`; the icon layer mapped it to `attention`. Now both layers are aligned. **Pattern:** When adding new notification subtypes, check both layers.

## Wiki Corrections Applied

### notification-dwell.md
Replaced "Two speeds to green: consensus (~15s) or aged-out (2 min)" with accurate `MaxDoneTime` (90s) status-time bypass description. Added explicit note that `TeammatePresenceTimeout` gates suppression, NOT promotion.

### teammate-detection.md
Updated "Teammates age out" row to describe `MaxDoneTime` bypass mechanism. Clarified that 2-minute `TeammatePresenceTimeout` is a suppression gate, not a promotion trigger.

### hook-events.md
Added section documenting both elicitation_dialog payload paths (`hook_event_name = "Elicitation"` and `hook_event_name = "Notification"` with `notification_type = "elicitation_dialog"`), both yielding `permission` status.

### CLAUDE.md — Notification Dwell section
Corrected "two speeds to green" from 2-min age-out to actual 15s quiet-path + 90s `MaxDoneTime` bypass mechanism.

## Future Configurability

`MaxDoneTime` (90s) is currently a private constant in `TrayApp` awaiting user reports of false promotions. Promotion path to config: add `teaming.maxDoneTimeSeconds` integer field to `ImrdyConfig` with suggested range 30s–300s (follows the same pattern as existing `TeammatePresenceTimeout` and `TeammateQuietThreshold` config). Do not add config plumbing until calibration data exists (no user complaints yet; 90s comfortably exceeds observed 30s teammate-sequence latency with 3x headroom).
