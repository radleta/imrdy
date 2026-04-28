# Schema

## Page Conventions

All knowledge pages require YAML frontmatter:

```yaml
---
tags: [domain/subtopic]
updated: 2026-04-DD
summary: "One-line description"
---
```

**tags:** Domain/subtopic (e.g., `imrdy/wsl`, `imrdy/dashboard`)
**updated:** Last modification date — staleness signal
**summary:** One-line description for the `## Pages` index

## Page Types

- **Research**: Factual findings about platform behavior, system architecture, or codebase discovery
- **Gotcha**: Counter-intuitive behavior or surprising constraint discovered during implementation
- **Pattern**: Reusable approach that works well and should be standardized
- **Drift**: Wiki/doc content found to be stale or inconsistent with current reality

## Linking

Use standard markdown links: `[Page Title](page-file.md)` — not wikilinks.

## Organization

Pages live as flat siblings alongside SKILL.md. No deep nesting.
