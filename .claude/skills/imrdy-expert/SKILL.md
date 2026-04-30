---
name: imrdy-expert
description: "imrdy project knowledge base — architecture decisions and behavior discovered from real usage."
---

You are an expert in the imrdy project.
Use the wiki below as your knowledge base.
For deeper detail on any topic, use the Read tool on the linked pages
resolved relative to the wiki path in the `<!-- wiki: ... -->` comment.

!`wiki-resolve imrdy-expert`
<!-- To update this wiki: /wiki-memory ingest imrdy-expert -->
<!-- If wiki-resolve is not installed, run: bash ~/.claude/skills/wiki-memory/scripts/install.sh -->

## Pages

- [State-Matrix Fixes](state-matrix-fixes.md) — 9 bugs fixed: notification dwell, consensus promotion, overlay interaction; architectural updates to constant roles and promotion matrix
- [MaxDoneTime Configuration Strategy](maxdonetime-configurability.md) — MaxDoneTime consensus delay remains constant pending user reports of false promotions
- [WSL→Windows PATH Passthrough Baseline](wsl-interop-baseline.md) — WSL distro-specific PATH passthrough varies; explicit verification needed
- [WSLENV Distro Identity Gap](wslenv-distro-not-forwarded.md) — Windows binaries can't self-identify source distro via WSLENV
- [WSL_DISTRO_NAME Env Var Gotcha](wsl-distro-env-var-gotcha.md) — WSL_DISTRO_NAME env var requires explicit pickup via IHookEnvironment; code exists but fallback was never wired
- [HookServiceBuilder Relocation](hook-service-builder-relocation.md) — Moved to Imrdy.Core.Hooks to enable cross-platform consumer access

## Meta

- [Operations Log](log.md) — Timestamped wiki operations log (ingest, lint, query filings)
- [Schema](schema.md) — Wiki conventions and page-type definitions
