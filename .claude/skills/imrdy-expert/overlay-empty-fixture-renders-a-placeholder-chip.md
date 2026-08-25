---
tags: [imrdy-expert/overlay]
summary: "The overlay empty fixture renders a visible placeholder chip — intended by Decision 6"
---

## The overlay `empty` fixture renders a visible chip — that is Decision 6's placeholder, not a rendering bug

During step 10's visual seal, `tests/fixtures/overlays/empty.json` contains a literal `[]`, but the rendered `empty.png` shows a single dim green circular chip rather than an empty panel. This reads as a rendering defect on first sight and costs several tool calls to disprove.

It is intended behavior. `OverlayPanel.ApplyItemsAndSize` computes `var count = Math.Max(1, items.Count)` with the comment "Minimum 1 chip slot for the idle placeholder when items is empty (Decision 6)" (`src/Imrdy.Windows/Overlay/OverlayPanel.cs:905-906`).

`OverlayPanel.OnPaint` routes the zero-item case to `PaintPlaceholderChip(g)` (`OverlayPanel.cs:297-300`), which draws a single dimmed imrdy glyph using `placeholderChipAlpha = 50` and `placeholderGlyphAlpha = 0.30f` (`OverlayPanel.cs:393-420`).

The rationale recorded in the source is that the panel must never render zero-width or invisible — a zero-session overlay that vanishes entirely would look like a crashed tray rather than an idle one.

General lesson for future visual seals: Before filing a rendered-output surprise as a defect, grep the painting code for the fixture's edge-case name. A fixture named for an empty/zero state is the most likely place for a deliberate placeholder path, and the deliberate path is usually commented with its decision id. This pattern holds across all UI surfaces, not just overlay — check the decision reference before opening a bug report.

For future seals: Prior to step 10 the only visual-seal baseline on disk was `scratch/imrdy-wsl/visual-seal/`, which holds 13 PNGs — dashboards only. The overlay and workspace-dashboard fixtures had never been visually inspected, so there was no baseline to diff `empty.png` against. Step 10 is the first seal covering all 19 fixtures (13 dashboards + 2 workspace-dashboards + 4 overlays).
