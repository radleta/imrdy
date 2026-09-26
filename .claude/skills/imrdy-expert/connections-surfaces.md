---
tags: [imrdy-expert/publishing]
summary: "The Connections window and imrdy links on both binaries render one ConnectionsViewModel; imrdy links exits 1 only on live health from the tray; ConnectionsForm is deliberately not a hover-dashboard peer"
last-verified: "2026-09-26"
---

# Connections Surfaces

## One view model, three consumers

[`ConnectionsViewModelBuilder.cs` `Build`](../../../src/Imrdy.Core/Publishing/ConnectionsViewModelBuilder.cs)
outer-joins `publishers.json`, the outbound sink health, the inbound `WireListener` links and the
file-sink heartbeats into one row per machine carrying **both** directions, and
`ConnectionRowFormatter` decides the cell strings once. It takes `now` and precomputes
`ConnectionRow.LastDelivery`, so no consumer reads the store, the config or the clock — the
[VM-as-Complete-Render-Contract](vm-as-complete-render-contract.md), and what makes the `connections`
render component deterministic ([Render Verb Architecture](render-verb-architecture.md)).

Its consumers are the `ConnectionsForm` window (controller menu → `Connections…`), `imrdy links` on
Windows (Spectre table or `--json`), and `imrdy links` on Linux (`LinksReport.RenderLines`, plain
stdout or `--json`). A new surface consumes the view model; it does not re-read the stores.

## The exit code is only a guard with live health

`LinksReport.ExitCode` returns 1 on a failed link, but a CLI process holds no sinks and no listener,
so the records-only view it builds can never contain one. `imrdy links` on Windows therefore asks
the running tray first over the `links-live` pipe verb and falls back to records only when no tray
answers — and with the pipe off by default in production, records-only is the normal case there.
Every run prints `LinksReport.HealthSource` saying which case it was (to stderr under `--json`);
Linux is always records-only. Read that line before trusting an exit 0. The pipe side is on
[Tray IPC](inspect-ipc.md#links-live); the reasoning is in
[`LinksReport.cs`](../../../src/Imrdy.Core/Publishing/LinksReport.cs).

## `ConnectionsForm` is not a hover dashboard

[`ConnectionsForm.cs`](../../../src/Imrdy.Windows/Connections/ConnectionsForm.cs) does not derive
from `HoverDashboardFormBase` and copies none of its lifecycle: it is sizable, in the taskbar and
activatable, created once and hidden on close rather than recreated per show. Its class remarks and
`OnFormClosing` say why, and `ConnectionsFormTests` pins the window shape — do not give it the
dashboard focus guard.

A new imrdy window that keeps a native caption or can show a scrollbar needs
`ImrdyPalette.ApplyDarkTitleBar` and `ImrdyPalette.ApplyDarkScrollbars`: a scrollbar paints from the
OS theme whatever the control's colours are, so a dark window on a light-theme machine otherwise
shows a white bar.

The links themselves, and what the rows report, are on [Cross-Machine Publishing](cross-machine-publishing.md)
and [Publisher Liveness](publisher-liveness.md).
