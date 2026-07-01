# Operations Log

## 2026-06-27 ingest | config live-reload FSW behavior

- New page: config-live-reload.md (config.json FSW → CONFIG_RELOAD token → OnConfigChanged; full reload scope: sound + icon style + tray god toggle + overlay; startup uses LoadSoundConfig separately; FSW subscribes Changed+Created for AtomicFileWriter delete-then-move)
- SKILL.md ## Pages updated with config-live-reload entry after state-file-write-path
- Source: TrayApp.cs fix that routed config FSW drain through OnConfigChanged instead of sound-only LoadSoundConfig; verified live in tray (overlay.enabled toggle applies without restart)

## 2026-05-16 ingest | WT desktop routing page + sweep-timer drift fix

- New page: wt-desktop-routing.md (3-step SwitchToSessionDesktop, pinned-vs-dynamic target, WT exclusion from dynamic lookup, compare-desktops focus guard, SessionStart-only auto-lock, residual race-loss reference)
- Fixed drift in architecture.md `## Timer Interactions`: sweep paragraph and timer table cell — was describing pre-4702e86 re-read behavior, now describes existence-check-only via `CleanupGoneSessions`
- SKILL.md ## Pages updated with wt-desktop-routing entry near persistence cluster
- Cleaned up now-stale "Drift alert" callout + closing cross-ref annotation in field-preservation-catalog.md (architecture.md is now consistent — callout reframed as generic "code wins over docs" precedence rule)
- Source: desktop-persist-fix + wt-desktop-routing + ping-pong guard implementation sessions (2026-05-15 / 2026-05-16); user-driven skill-builder assess run

## 2026-05-15 ingest | persistence architecture artifacts

- New page: state-file-write-path.md (atomicity asymmetry across the 3 JSON surfaces; why session state bypasses AtomicFileWriter)
- New page: tray-hook-write-race.md (RMW race window between hook and tray writes on session state; architectural framing as shared-data-source anti-pattern)
- New page: tray-persistence-verbs.md (catalog of every tray-owned write surface — debugging checklist)
- New page: field-preservation-catalog.md (authoritative 6-field list; symmetry contract; audit procedure)
- Refreshed architecture.md Field Preservation section (was stale at 4 fields; code has 6 — added StartedAt and WslDistro; added cross-links to new pages)
- SKILL.md ## Pages updated with all 4 new entries
- Source: user-reported diagnosis task — "changes by tray don't seem to be fully persisted to the json file"; root architectural cause identified as shared-data-source pattern between hook and tray on session state files

## 2026-05-15 cleanup | SKILL.md modernization

- Stripped legacy `!wiki-resolve imrdy-expert` directive and two HTML scaffolding comments
- Replaced verbose 4-line role prose with single-paragraph canonical role stub
- SKILL.md now matches canonical wiki-backed structure (frontmatter → role stub → ## Pages → ## Meta)

## 2026-05-15 cleanup | partial-migration → healthy (--full)

- Added bidirectional cross-references between hook-events.md and status-mapping.md (Step 5b deep-scan finding)
- Deleted legacy `.wiki-memory/imrdy-expert/` directory — all 24 files removed; content now lives exclusively at `.claude/skills/imrdy-expert/`
- `.wiki-memory/` itself was empty after deletion and was also removed
- wiki-health imrdy-expert --full now returns healthy

## 2026-05-15 migrate | partial-migration → healthy

- Migrated 21 pages from legacy `.wiki-memory/imrdy-expert/` into `.claude/skills/imrdy-expert/`
- Fixed tag prefix `imrdy/...` → `imrdy-expert/...` on all 25 pages (4 pre-existing + 21 migrated)
- Stripped deprecated `updated:` frontmatter per WMF-D4 (page staleness comes from git/mtime)
- Rewrote `inspect-ipc.md` frontmatter from learned-file shape to standard page shape
- Updated SKILL.md ## Pages to list all 25 pages
- Updated schema.md to remove `updated:` field requirement and use correct tag prefix in examples
- Legacy `.wiki-memory/imrdy-expert/` left in place for user review

## 2026-04-29 ingest | WSL_DISTRO_NAME env var gotcha

- New page: wsl-distro-env-var-gotcha.md (WSL distro identity extraction from env var)
- Pages index updated: added entry to SKILL.md ## Pages
- Source: research-milestone-a-wsl-distro-env-var-not-populated.md (Step 06b hot-fix discovery)

## 2026-04-28 Migration

- Migrated from old-format wiki to wiki-backed skill via skill-edit (imrdy-WSL Step 04 post-approval)
- Pages: wsl-interop-baseline.md, wslenv-distro-not-forwarded.md moved from old wiki
2026-06-25T21:11:50Z created overlay-rendering-internals
2026-06-25T21:12:38Z updated display-item-vs-session-identity
2026-06-26T21:22:45Z created displayitem-source-gen-gotcha
2026-06-26T21:22:53Z created stj-source-gen-interface-caveat
2026-06-26T21:22:59Z created render-fixture-offscreen-pattern
2026-07-01T16:48:01Z created internals-visible-to-mechanism
