---
tags: [imrdy-expert/icons]
summary: "Tray icon renderers: ParametricShapeRenderer and PackIconRenderer behind ITrayIconRenderer, built per style by TrayIconRendererFactory — 'dots' is the config default and must be normalized before comparing, and a bad pack or unknown style silently renders circles"
last-verified: "2026-09-26"
---

# Tray Icon Rendering

## Two renderers, one per style

`ITrayIconRenderer` has two implementations in `src/Imrdy.Windows/Icons/`:

- `ParametricShapeRenderer` draws the built-in GDI+ shapes (`StyleNames.BuiltInStyles`) and is the
  fallback every failure lands on.
- `PackIconRenderer` renders an SVG graphics pack from `~/.imrdy/graphics/packs/<name>/pack.json`,
  loaded by `GraphicsPackLoader`, which follows the sound `PackLoader`.

[`TrayIconRendererFactory.cs` `Create`](../../../src/Imrdy.Windows/Icons/TrayIconRendererFactory.cs) builds one renderer per style name (`"<built-in>"` or
`"pack:<name>"`), and `TrayApp` caches them by style, case-insensitively. Which style a given
session uses is the chain on [Architecture](architecture.md#session-icon-style-resolution).

## `dots` is the default, and it is an alias

`ConfigReader.EnsureDefaults` writes `tray.iconStyle: "dots"` when the key is empty, and
[`StyleNames.cs` `NormalizeStyleName`](../../../src/Imrdy.Core/Icons/StyleNames.cs) maps `dots`
(any case) to `circles`. Normalize before comparing or keying on a style name, or a fresh config's
`dots` and a menu-chosen `circles` read as two styles.

## Every failure renders circles

- An unknown built-in name falls through the factory's `switch` to circles with no log line.
- A `pack:<name>` that fails the path-traversal check, fails to load, or loads unhealthy falls back
  to circles with one Warning (`falling back to circles`).

Nothing reaches the UI in either case. When a configured pack shows circles, read the tray log for
that Warning before debugging the SVG.

## Aging and the disconnected variant are baked in

Unlike the overlay, which applies aging as paint-time opacity
([overlay-rendering-internals](overlay-rendering-internals.md#aging-tier-and-onpaint-opacity-ladder)),
the tray renderers bake the aging tier into each icon bitmap. `GetIcon(status, ageTier,
disconnected)` takes the disconnected flag as a third cache key; what sets it is
[Publisher Liveness](publisher-liveness.md).
