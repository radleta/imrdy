---
tags: [imrdy-expert/rendering]
summary: "`imrdy render --all --output-dir` writes files flat despite grouped console output — all PNGs land directly in output-dir, not in component subdirectories"
code-cites:
  - src/Imrdy.Windows/Commands/RenderCommand.cs
  - src/Imrdy.Core/Rendering/RenderContext.cs
---

# Render Output-Dir Flat Layout

## The Gotcha

`imrdy render --all --output-dir <dir>` prints console output that *appears* to use a component subfolder structure:

```
dashboard/aged-done.png 520x392
overlay/mixed-status.png 374x72
```

However, the actual files are written **flat** directly to `<dir>/`, not in component subdirectories:

```
<dir>/aged-done.png        (not <dir>/dashboard/aged-done.png)
<dir>/mixed-status.png     (not <dir>/overlay/mixed-status.png)
```

The `component/` prefix in the console output is a **display label only**, not a path segment.

## Impact

Any code or tool that pipes `--output-dir` output into subsequent steps (reading fixture paths, processing PNGs, etc.) must construct paths as `<output-dir>/<fixture-name>.png`, not `<output-dir>/<component>/<fixture-name>.png`.

**Collision risk:** Fixture names are currently unique across components, so the flat layout does not collide. However, if two components ever share a fixture name, the second PNG write would silently overwrite the first — a silent data loss with no error. Future work adding new components should validate fixture-name uniqueness across all components before changing the layout.

## Discovery

Encountered during step 03 visual-seal verification. Attempted to read `overlay/mixed-status.png` via the path implied by the console output, which failed with "File does not exist". Running `find` over the output directory revealed all PNGs in the flat root with no subfolders.

## Related

- [Render Verb Architecture](render-verb-architecture.md) — overall render command design
