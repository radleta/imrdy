---
tags: [imrdy-expert/logging]
summary: "build-dev.sh writes ~/.imrdy/.dev-build (holding the repo root) after every dev deploy; every imrdy process that sees it logs at Debug, the inspect pipe defaults on, and imrdy render resolves fixtures and output from its path"
last-verified: "2026-09-25"
---

# Dev Build Marker & Logging

## The Marker

`build-dev.sh` writes `~/.imrdy/.dev-build` (`ImrdyPaths.DevBuildMarker`) on every deploy, on both the Windows and Linux branches. The file **holds the repo root path** — Windows form (`D:/…`, via `pwd -W`) on Windows, because the MSYS form (`/d/…`) fails `Directory.Exists` in .NET. It is never removed automatically.

Three things read it:

| Reader | Effect |
|---|---|
| `ServiceRegistration.AddSerilog` | Minimum level Debug instead of Information, for every process that builds logging — hook, tray, CLI, daemon |
| `DiagnosticsConfig.IpcEnabled ?? File.Exists(DevBuildMarker)` | The `Local\ImrdyInspect` pipe starts by default (see [Tray IPC](inspect-ipc.md)) |
| `RenderCommand` and the tray's Manage → Dev menu | The file's contents are the repo root that fixtures (`tests/fixtures/…`) and default render output (`scratch/views/…`) resolve against (see [Render Verb Architecture](render-verb-architecture.md)) |

Presence alone drives the log level and the pipe default; only the render and Dev-menu paths read the contents.

## Log Level Resolution

In `AddSerilog`: `verbose` → Debug, `quiet` → Warning, otherwise Information — and then, **only when neither `verbose` nor `quiet` was passed**, `IMRDY_LOG=1` or the marker's presence raises it to Debug. The file sink rolls at 1 MB and keeps 5 files (`shared: true`, so concurrent hook processes can write one log). Logs land in `~/.imrdy/logs/` as `monitor_*.log` (tray), `hook__*.log` (hook) and `daemon__*.log` (Linux daemon).

## Why a File and Not `IMRDY_LOG=1`

An environment variable reaches only processes started from the shell that set it. The hook is started by Claude Code, not by the developer's shell, and the tray is spawned by a hook (`TraySpawner`) or detached by `build-dev.sh` through `cmd //c start`. A variable exported before `./build-dev.sh` therefore reaches neither the hooks nor, reliably, the tray that serves them. A file on disk is read by every process at startup, whoever launched it.

## Turning It Off

```bash
rm ~/.imrdy/.dev-build
```

The next process to start logs at Information and keeps the inspect pipe off (unless `diagnostics.ipcEnabled` is set). The marker survives a plain `dotnet publish` — only `build-dev.sh` writes it and nothing deletes it — so a machine that ever ran `build-dev.sh` stays in dev mode until the file is removed. Remove it to test production-like log levels or the records-only `imrdy links` path.

## What Debug Adds

Debug-level lines include hook raw stdin payloads, the hover controllers' state heartbeat (every 10th drain tick, with cursor, overlay bounds, dwell and cooldown state) and their enter/exit transitions, COM virtual-desktop candidate probing, and foreground-capture decisions for context menus. When hover, focus or desktop behavior looks wrong, turn the marker on and read those lines before reading code.
