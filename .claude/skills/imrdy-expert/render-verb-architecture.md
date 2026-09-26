---
tags: [imrdy-expert/rendering]
summary: "imrdy render verb: in-process PNG capture of WinForms surfaces without a screen — layer split, Program.cs placement, the offscreen-Show capture sequence, output layout, sequential STA execution"
last-verified: "2026-09-25"
---

# Render Verb Architecture

## Overview

`imrdy render <component> [--output <path> | --output-dir <dir>]` produces deterministic PNG artifacts of WinForms UI surfaces without a running tray process. Four registered components (`RenderRegistry.Components`), all captured via `Form.DrawToBitmap`:

| Component | Form | Fixture directory (`DefaultFixtureDir`) |
|-----------|------|-------------------|
| `dashboard` | `SessionDashboardForm` | `tests/fixtures/dashboards` |
| `workspace-dashboard` | `WorkspaceDashboardForm` | `tests/fixtures/workspace-dashboards` |
| `overlay` | `OverlayPanel` | `tests/fixtures/overlays` |
| `connections` | `ConnectionsForm` via `NullConnectionsHost` | `tests/fixtures/connections` |

`tests/fixtures/dashboards-bad/` holds invalid fixtures for the renderer's validation path; `--all` does not read it. `ConnectionsRenderer` pins an explicit `ClientSize` because that window is resizable, unlike the other three.

Key commands:
- `imrdy render <component> <fixture.json>` — render a single fixture
- `imrdy render --list` — enumerate registered components
- `imrdy render --all [--output-dir <dir>]` — render every fixture of every component

**Adding a fixture or a component is a two-place change.** `RenderCommandAllTests` hardcodes the per-component fixture counts *and* a summary-line prefix filter. Miss the filter and the PNG assertion passes while the summary-line assertion fails by exactly the new fixture count — which reads as "the renders did not run" when they did. Count what `--all` will render with `ls tests/fixtures/{dashboards,workspace-dashboards,overlays,connections}/*.json | wc -l`.

## Output Layout

- **No `--output-dir`:** each component writes to `{repoRoot}/scratch/views/{component}/`, where `repoRoot` is the path stored in the `~/.imrdy/.dev-build` marker; without a marker, `./scratch/views/{component}/` under the current directory. Fixture directories resolve against the same root.
- **`--all --output-dir <dir>`:** every component writes **flat** into `<dir>`. The console prints `dashboard/aged-done.png 520x392`, but the `component/` prefix is a display label, not a path segment — the file is `<dir>/aged-done.png`. Anything that consumes the output must build `<dir>/<fixture-stem>.png`.

Because the flat layout keys on the fixture stem alone, two components with a fixture of the same name would overwrite each other's PNG silently. Fixture stems are unique across the four directories; keep them so when adding one.

## Layer Split (D1)

Pure contracts live in `Imrdy.Core/Rendering/`:
- `IRenderableSurface` — interface a renderable component implements
- `RenderContext` — input (fixture args, output path, logger factory, repo root)
- `RenderResult` — output (success, error, width, height)

No WinForms types cross into Core. `RenderCommand` (`Imrdy.Windows/Commands/`) and the concrete renderers plus `RenderRegistry` (`Imrdy.Windows/Rendering/`) are WinForms-dependent.

## Program.cs Branch Placement

The `"render"` branch is placed BETWEEN `preview-dashboard` and the bare-tray fallback:

1. `hook` — fast path, no WinForms
2. `CommandRouter` — Spectre CLI, no WinForms
3. `preview-dashboard` — WinForms dev tool, bypasses mutex
4. `render` — WinForms dev tool, bypasses mutex  ← here
5. Tray — full app, mutex-gated

The Spectre CLI branches skip WinForms init; render needs it. The render branch runs the same three init lines as preview-dashboard: `SetHighDpiMode`, `EnableVisualStyles`, `SetCompatibleTextRenderingDefault(false)`.

## Mutex Bypass Rationale

`Global\ImrdyMonitor` is NOT checked for render (same as preview-dashboard). Render is a dev tool that must run while the live tray is running — after `build-dev.sh` deploys a new binary, the dev immediately runs `imrdy render --all` to inspect PNG output. Requiring the tray to stop first would break that workflow.

## The Capture Sequence

`CreateControl()` alone gives a form a handle, but its child controls never run their paint cycle without the message pump, and `DrawToBitmap` then captures only the form background. Every renderer therefore uses the offscreen-Show sequence:

1. Construct the form with its headless collaborators: the dashboards take `desktopManager: null` (no COM desktop interop, no all-desktops pinning), the overlay a `NullSessionInteractionRouter`, the connections window a `NullConnectionsHost`.
2. `StartPosition = FormStartPosition.Manual`; `Location = new Point(-32000, -32000)` (effectively hidden on every monitor, no flicker, no activation).
3. `Show()`.
4. `Application.DoEvents()` to drain the pending paint cycle for every child, then `PerformLayout()` (`ConnectionsRenderer` drains once more after it).
5. `DrawToBitmap` into a bitmap of the form's size and save the PNG.
6. `Hide()` in a `finally`; the form is disposed by its `using`.

Further `DrawToBitmap` caveats:

- **DWM mica/acrylic does NOT render** — the backdrop targets the compositor, not the GDI layer; PNGs show the form background color instead.
- **Low-alpha decorative lines can vanish** — see [DrawToBitmap Alpha Compositing](drawtobitmap-alpha-compositing.md).
- **Font rendering uses GDI+ metrics, not ClearType** — output is representative but not pixel-identical to on-screen rendering.
- **Fixture types must be registered with `ImrdyJsonContext`** — see [Source-Generated JSON Registration](source-gen-json-registration.md).

## Sequential STA Execution (D5)

All fixtures for all components render sequentially on the main STA thread. No parallelism: `DrawToBitmap` and WinForms controls are STA-affine. Ctrl-C (`Console.CancelKeyPress`) sets a flag checked between fixtures; a cancelled run exits 130.

## Inline DI (D3)

`RenderCommand.Run` builds an inline `ServiceCollection` (same as `PreviewDashboardCommand`) rather than a shared service builder. `HookServiceBuilder` and `MonitorServiceBuilder` exist because the tray and hook are distinct long-running processes; preview-dashboard and render are short-lived dev tools, and a shared builder for two callers would be premature.

## Visual Seal Protocol

For any UI-bearing change (SessionDashboardForm, WorkspaceDashboardForm, ConnectionsForm, overlay, tray icons, menus), run `imrdy render --all` after a successful build and inspect every PNG before declaring the work complete. A passing verifier wave is NOT a substitute: layout-collapse bugs (controls rendered at zero size) pass every non-visual gate. See the user-scoped `verify-fix-loop-expert` wiki for the full four-gate protocol.

### Render with the binary you just built

The bare `imrdy` on PATH is `~/.local/bin/imrdy.exe`, the binary the last `./build-dev.sh` deployed. `dotnet build` does not touch it. So `dotnet build` + `imrdy render --all` renders with the previously deployed code and reports "defects" that are already fixed in source — a stale grip-less overlay at the old panel widths was once filed exactly that way.

Run `./build-dev.sh` immediately before `imrdy render --all`, or invoke the just-built Debug exe by path: `src/Imrdy.Windows/bin/Debug/net10.0-windows10.0.17763.0/win-x64/imrdy.exe render --all`.

## Related

- [Dev Build Marker & Logging](dev-build-marker-logging.md) — `.dev-build` controls both the default output root and debug logging
- [Hover Dashboard Form Lifecycle](hover-dashboard-form-lifecycle.md) — the dashboard forms render captures
- [xunit Parallel Console Redirects](xunit-parallel-console-redirect.md) — why the render CLI tests share one `[Collection]`
