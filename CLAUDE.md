# imrdy

Windows system tray monitor for Claude Code sessions. .NET 10, WinForms, single executable, plus a Linux hook/publisher binary.

## Why

Running several Claude Code sessions at once is an attention problem. imrdy puts one tray icon per session in peripheral vision: color is state (green waiting with nothing running, teal waiting with agents running, busy, attention, permission, error), icons dim as sessions go quiet, and a click switches to the session's virtual desktop and focuses its terminal.

## Where knowledge lives

Architecture, behavior and gotchas live in the `imrdy-expert` wiki (`.claude/skills/imrdy-expert/`). Load it before changing the hook, state files, the tray/overlay/dashboards, cross-machine publishing, render/IPC, config sections, source-gen JSON, `build-dev.sh`, or COM desktop interop.

```
src/Imrdy.Core/          Platform-independent: state files, sound, config, menus, validation, publishing
src/Imrdy.Windows/       WinForms tray app, COM desktop interop, CLI commands, hook command
src/Imrdy.Linux/         Linux binary: hook + publisher daemon + links (no UI)
tests/Imrdy.Core.Tests/  Unit tests (xunit + FluentAssertions)
tests/Imrdy.Windows.Tests/  WinForms unit tests
tests/Imrdy.Integration.Tests/  Integration tests (require built binary)
```

## Rules

- Every Spectre-CLI verb must also be in the `args[0] is ...` pattern in `Program.cs`; otherwise it silently starts a tray.
- Every user-initiated session/workspace interaction goes through `ISessionInteractionRouter`. Event handlers never call `SwitchToSessionDesktop`, `SwitchToWorkspaceDesktop`, `menu.Show` or `NotifyIconMenuHost.Show` directly.
- For any UI-bearing change (dashboards, `ConnectionsForm`, overlay, tray icons, menus), run `imrdy render --all` after building and inspect every PNG before calling it done.
- Anything in Core that the Linux daemon uses must work on both `build-dev.sh` branches.

## Build & Test

```bash
dotnet build
dotnet test --filter "Category!=Integration&Category!=Benchmark"   # unit tests
dotnet build src/Imrdy.Linux/Imrdy.Linux.csproj -r linux-x64       # Linux hook + daemon
./build-dev.sh                                                     # publish, deploy to ~/.local/bin/, restart tray/daemon, touch ~/.imrdy/.dev-build
```

Target `net10.0-windows10.0.17763.0` / `net10.0`, PublishSingleFile + SelfContained, no IL trimming (WinForms is incompatible).

## Conventions

- Nullable, ImplicitUsings and TreatWarningsAsErrors are on (`Directory.Build.props`); file-scoped namespaces are enforced as errors.
- `_camelCase` private fields, PascalCase public members; 4-space indents for code, 2-space for XML/JSON/YAML.
- CLI commands are static classes with `Run(ServiceProvider, ...)` and write through `IAnsiConsole`.
- All paths come from `ImrdyPaths`; config writes go through `AtomicFileWriter`.

## Git workflow

- `develop` for work, `main` for releases and PRs. Tags: `v*` binaries, `pack-*` sound packs.
- Reconcile a diverged branch by merge, never rebase, even though `develop` looks linear. Preview with `git merge-tree --write-tree <upstream> HEAD`, and read a clean `TrayApp.cs` merge semantically, since independent changes share the 100ms drain tick.
