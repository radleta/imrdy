---
tags: [imrdy-expert/publishing]
summary: "When a remote session reads as disconnected: a TCP publisher answers from its inbound link, a file-sink publisher from its heartbeat file, a missing beat reads as connected, nothing is persisted — plus heartbeat format, deploy order, and the geometric disconnected glyph"
last-verified: "2026-09-26"
---

# Publisher Liveness

## One meaning, two signals

[`TrayApp.cs` `IsPublisherDisconnected`](../../../src/Imrdy.Windows/TrayApp.cs) answers false for a
local session. For a remote one it uses the signal that session's transport has:

- **TCP publisher** — if `WireListener` holds an inbound link under that machine name, its answer is
  final: disconnected means `SinkState.Failed`, which the listener sets when the socket drops.
- **File-sink publisher** — it opens no connection and never appears in that table, so it writes a
  **heartbeat** and `HeartbeatWatch.IsDisconnected` reads it. Without the beat, a terminated WSL
  distro rendered exactly like a quiet one.

The answer is recomputed per render and never written into session state, for the same reason as
`DisplayStatus.Resolve` ([Teammate Detection](teammate-detection.md), "Display resolution").
The publishing stack itself is on [Cross-Machine Publishing](cross-machine-publishing.md).

## The heartbeat

[`PublisherHeartbeat.cs`](../../../src/Imrdy.Core/Publishing/PublisherHeartbeat.cs) is the one
definition of the beat's path, format and thresholds, and its class remarks carry the reasoning.

- **Location:** one `<token>.hb` file per publisher in a `heartbeats` directory **beside** the
  receiver's `sessions` directory, never inside it — everything in `sessions/` is enumerated as a
  session.
- **Format:** two lines, the publisher's machine name (escaped and bounded like `origin_machine`,
  because the filename token is lossy) then an ISO-8601 timestamp. `HeartbeatWatch.Beats` exposes the
  name as `MachineBeat.Name`.
- **Writer:** `DaemonHost` beats every `Interval` from its existing loop tick, **unconditionally** —
  never gated on session activity, so a quiet but live publisher reads as alive.
- **Threshold:** `StaleAfter` is derived from measurements in that constant's XML doc. Do not restate
  its value in prose — a README line did and went stale the first time it moved. Re-derive it rather
  than nudging it, and raise `StaleAfter`, never `Interval`, if the `build-dev.sh` redeploy gap
  grows.
- **A missing beat file reads as connected.** A publisher that does not beat — an older build, or the
  Windows tray, which is a TCP publisher and deliberately does not — keeps its prior rendering
  instead of a permanent false alarm. This is not inference from silence: a beat is an explicit
  signal on a fixed interval from the exact process being measured.

**Deploy the receiver before its publishers.** A current receiver parses an older timestamp-only
beat (named by its token), but a receiver built before the name was carried
(commit 88cfb84) rejects the two-line
beat and reads it as absent. That fails open to connected, so a stopped publisher loses its
disconnected glyph and its Connections row with nothing saying why. `build-dev.sh` relaunches a
running daemon, which writes the new format at once.

## Rendering: geometry, not opacity

Disconnected is a **third cache key, never a sixth aging tier** — the aging tier already owns
opacity. [`DisconnectedGlyph.cs`](../../../src/Imrdy.Windows/Icons/DisconnectedGlyph.cs) shrinks the
glyph to 60% and draws a dashed ring in the glyph's averaged colour.

- Tray: `ITrayIconRenderer.GetIcon` takes the flag; `AgingCache` keys on it, `PackIconRenderer`
  pre-renders every `(status, tier, disconnected)` combination, and every unknown-status fallback
  carries it ([Tray Icon Rendering](tray-icon-rendering.md)).
- `SessionEntry.LastDisconnected` pairs with `LastAgingTier`, so the aging tick's re-render guard
  also fires when a link flips.
- Overlay: the chip bitmap cache keys on it too
  ([overlay-rendering-internals](overlay-rendering-internals.md#bitmap-cache)), read from
  `DisplayItem.IsDisconnected` ([DisplayItem vs SessionEntry Identity](display-item-vs-session-identity.md)).
