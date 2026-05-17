---
tags: [imrdy-expert/rendering]
summary: "DrawToBitmap requires higher alpha for decorative lines than runtime DWM compositing"
---

## DrawToBitmap Alpha Compositing: Very-Low-Alpha Colors Disappear

`Form.DrawToBitmap` (used by `DashboardRenderer`) composites children onto a pre-filled background color — it does not start from a transparent surface. GDI+ Pen/Brush with alpha below ~30 will render as nearly invisible because the compositing arithmetic yields a final pixel that is indistinguishable from the background color.

The design system `Border` constant (`Color.FromArgb(20, 255, 255, 255)`, ~8% white) is intentionally subtle at runtime (where DWM composites over a blurred backdrop). In the static PNG render path the backdrop is absent, so the same alpha value renders invisible.

**Rule:** When painting decorative elements (border-left lines, separator lines) via `OnPaint` or `Paint` events that must be visible in `DrawToBitmap` output, use alpha ≥ 60–80 for single-pixel or 2px lines. Do not blindly copy the CSS `var(--border)` alpha (typically 8–15%).

## Application

In `SessionDashboardForm.OnPaint` overrides and child Panel `Paint` event handlers:
- Border-left decorative lines: use `Color.FromArgb(80, 255, 255, 255)` or higher
- Separator lines: use `Color.FromArgb(100, 255, 255, 255)` or higher
- Nested child control Paint events: same rule applies if the control tree is rendered via `DrawToBitmap`

For runtime display in the tray dashboard, the lower alpha values render correctly via DWM; the render-verb path is the only place this discrepancy appears.

## Related

- [Render Verb Architecture](render-verb-architecture.md) — DrawToBitmap caveats and render-registry contracts
