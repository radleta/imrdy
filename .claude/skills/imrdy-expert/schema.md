# Schema

## Page Conventions

All knowledge pages require YAML frontmatter:

```yaml
---
tags: [imrdy-expert/subtopic]
summary: "One-line description"
---
```

**tags:** Domain/subtopic (e.g., `imrdy-expert/wsl`, `imrdy-expert/dashboard`)
**summary:** One-line description for the `## Pages` index

Page staleness is tracked via git log / filesystem mtime — no `updated:` field required.

**last-verified:** optional, a quoted date string (`last-verified: "2026-09-25"`) recording when the page's claims were last checked against the code — written only on a verification or a correction, never on an ordinary edit.

## Page Types

- **Research**: Factual findings about platform behavior, system architecture, or codebase discovery
- **Gotcha**: Counter-intuitive behavior or surprising constraint discovered during implementation
- **Pattern**: Reusable approach that works well and should be standardized
- **Drift**: Wiki/doc content found to be stale or inconsistent with current reality

## Linking

Use standard markdown links: `[Page Title](page-file.md)` — not wikilinks. Cite code as a link to the file with a stable token in the anchor text (``[`TrayApp.cs` `PersistSessionField`](../../../src/Imrdy.Windows/TrayApp.cs)``), never a line number.

## Organization

Pages live as flat siblings alongside SKILL.md, with at most one level of group folder (e.g. `testing/`) when a subject area warrants it.
