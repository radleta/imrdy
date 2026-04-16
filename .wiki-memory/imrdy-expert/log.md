# imrdy-expert Wiki — Operations Log

## [2026-04-16] ingest | Overlay interactivity research
- New page: overlay-interactivity.md (WS_EX_TRANSPARENT toggle pattern, NCHITTEST hit-testing, selective pass-through architecture)
- Updated: index.md (+1 page, last updated date)

## [2026-04-15] update | purple-sticking permission fix — TeammateGate
- Updated: teammate-detection.md (Layer 1 expanded: permission-clearing exception, TeammateGate class, ShouldClearPermission/ApplyTeammateEvent methods)
- Updated: hook-events.md (agent_id gate section: teammate events now delegate to TeammateGate, permission-clearing exception noted)
- Updated: CLAUDE.md (teammate-aware gating paragraph: unconditional "skip" replaced with exception for permission-resolution events, TeammateGate reference added)

## [2026-04-14] update | idle_prompt suppression + sweep optimization
- Updated: teammate-detection.md (3-layer → 4-layer, added Layer 4 idle_prompt suppression, revised speeds-to-green table)
- Updated: notification-dwell.md (expanded teammate-aware suppression section with idle_prompt detail)
- Updated: architecture.md (sweep timer 2s→10s, stale timer 30s→60s, added LastProcessedTimestamp skip optimization)
- Updated: hook-events.md (idle_prompt section clarified: solo-only backstop, suppressed for teams)
- Updated: status-mapping.md (done→idle promotion paths clarified for teams vs solo)
- Updated: index.md (teammate-detection summary updated to 4-layer)

## [2026-04-14] ingest | Initial seed from teammate-aware notifications session
- New page: hook-events.md (20 events mapped, behavioral discoveries from real testing)
- New page: teammate-detection.md (3-layer system, agent_id gate, consensus promotion, clawd-on-desk reference)
- New page: notification-dwell.md (dwell timers, defense-in-depth, teammate suppression)
- New page: status-mapping.md (event→status→color chain, "done" intermediate, aging tiers)
- New page: architecture.md (entry points, state lifecycle, field preservation, timer interactions)
- Index updated: +5 pages

## [2026-04-14] init | Wiki created
- Created: index.md, log.md, schema.md, .mditerc
- Domain registered in paths.env
