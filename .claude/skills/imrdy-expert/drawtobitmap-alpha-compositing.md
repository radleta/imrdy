---
tags: [imrdy-expert/rendering]
summary: "DrawToBitmap renders very-low-alpha decorative lines invisible — dashboard border and separator lines use alpha 80, not the 20-alpha Border constant"
last-verified: "2026-09-25"
---

## DrawToBitmap Alpha Compositing: Very-Low-Alpha Lines Disappear

`Form.DrawToBitmap` (used by every `imrdy render` component) renders alpha over an already-composited surface rather than a transparent one. A GDI+ `Pen` or `Brush` with a very low alpha therefore yields pixels indistinguishable from the background, and the line vanishes from the PNG.

`SessionDashboardForm` keeps a design-system `Border` constant of `Color.FromArgb(20, 255, 255, 255)` (~8% white), copied from the mockup's CSS border alpha. Drawn at that alpha, the last-prompt accent line and the desktop-chip outline disappear from rendered PNGs, so both `Paint` handlers draw with `Color.FromArgb(80, 255, 255, 255)` instead — and the comment at each site says why.

**Rule:** a decorative line (accent bar, separator, chip outline) painted in `OnPaint` or a `Paint` handler uses alpha ≥ 80 for 1–2px strokes. Do not copy a mockup's 8–15% border alpha straight into a pen; the visual seal would then inspect PNGs that are missing the line.

## Related

- [Render Verb Architecture](render-verb-architecture.md) — the capture sequence and other `DrawToBitmap` caveats
