# Operations Log

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
