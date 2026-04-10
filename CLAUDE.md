# imrdy

Windows system tray monitor for Claude Code sessions. .NET 10, WinForms, single executable.

## Why

Managing multiple Claude Code sessions in parallel is an attention problem: knowing which session needs you, which is working, which is idle, and acting on the right one without losing focus on your work.

imrdy puts that information in the system tray where it stays glanceable in peripheral vision:

- **Dots in the tray** — one icon per active session
- **Color = state** — busy, idle, needs attention, permission requested
- **Aging = dimming** — icons fade as sessions go quiet
- **Click = acknowledge and bring to focus** — switches to the session's virtual desktop and focuses its terminal in one gesture

## Architecture

```
src/Imrdy.Core/          Platform-independent: state files, sound, config, menus, validation
src/Imrdy.Windows/       WinForms tray app, COM desktop interop, CLI commands, hook command
tests/Imrdy.Core.Tests/  Unit tests (xunit + FluentAssertions)
tests/Imrdy.Integration.Tests/  Integration tests (require built binary)
```

### Three Entry Points (Program.cs)

```
imrdy hook        → HookCommand (fast-path, no WinForms, reads stdin JSON, writes state file)
imrdy <command>   → CommandRouter (status|packs|config|workspace|stop, Spectre.Console output)
imrdy             → TrayApp (WinForms ApplicationContext, Application.Run, message pump)
```

The hook runs hundreds of times per session. It uses `HookServiceBuilder` (lightweight DI, no COM/WinForms). The tray uses `MonitorServiceBuilder` (full DI with COM desktop manager).

### Graphics Packs

`ITrayIconRenderer` interface with two impls: `CircleIconRenderer` (built-in GDI+ dots, always-available fallback) and `PackIconRenderer` (SVG via Svg.NET v3.4.7). Config flag `tray.iconStyle` selects: `"dots"` or `"pack:<name>"`. Packs live at `~/.imrdy/graphics/packs/<name>/` with a `pack.json` manifest. `GraphicsPackLoader` in `Imrdy.Core` mirrors the sound `PackLoader`. Pack load failure silently falls back to dots.

### Overlay (Mode B)

`OverlayWindow` in `src/Imrdy.Windows/Overlay/` — borderless transparent `Form` with `WS_EX_LAYERED` for per-pixel alpha via `UpdateLayeredWindow`. Renders session characters as a horizontal row at the bottom screen edge. Uses `GraphicsPackLoader` directly to render SVGs at overlay size (or GDI+ circles in dots mode) — independent of `ITrayIconRenderer`. TopMost enforced by a 5-second watchdog via `SetWindowPos` P/Invoke. `PInvokeOverlay.cs` in `src/Imrdy.Windows/Desktop/` holds all overlay-specific Win32 declarations. Pre-rendered bitmap cache with aging (`ColorMatrix` desaturation). Config: `overlay.enabled`, `overlay.position`, `overlay.size`, `overlay.spacing`.

## Build & Test

```bash
dotnet build                                    # Debug build
dotnet test --filter "Category!=Integration&Category!=Benchmark"  # Unit tests only (333 tests)
./build-dev.sh                                  # Publish → stop tray → deploy to ~/.local/bin/ → auto-respawn
```

Target: `net10.0-windows10.0.17763.0` | PublishSingleFile + SelfContained | No IL trimming (WinForms incompatible)

## Key Conventions

- **Nullable=enable, ImplicitUsings=enable, TreatWarningsAsErrors=true** (Directory.Build.props)
- **File-scoped namespaces** enforced as error
- **_camelCase** private fields, **PascalCase** public members
- **4-space indents** for code, 2-space for XML/JSON/YAML
- CLI commands: static classes with `Run(ServiceProvider, ...)`, use `IAnsiConsole` for output
- All paths centralized in `ImrdyPaths` (config, sessions, logs under `~/.imrdy/`)
- Atomic file writes via `AtomicFileWriter` for config changes
- Source-generated JSON: `ImrdyJsonContext` (no reflection)

## Critical Constraints

**COM Virtual Desktop Interop**: Uses undocumented `IVirtualDesktopManagerInternal` with build-keyed GUIDs (`VirtualDesktopGuids.cs`). Gracefully degrades on unknown Windows builds. Recovers from Explorer restart via lazy re-init on COMException.

**Single Instance**: Mutex-gated via `MutexAcl.TryOpenExisting` (`Global\ImrdyMonitor`). Hook fast-path probes mutex to decide whether to spawn tray.

**Toast Notifications**: Uses `Microsoft.Toolkit.Uwp.Notifications` (WinRT toast API). Click activation fires on background thread — must marshal to UI via `BeginInvoke`. Extracts icon to `~/.imrdy/imrdy.png` for toast logo.

**Stop Signal**: Named `EventWaitHandle` (`Local\ImrdyStop`). `imrdy stop` signals it; tray listens on background thread, posts `ExitThread` to UI thread.

## Git Workflow

- **main**: releases, PR target
- **develop**: active development
- Tags: `v*` for binary releases, `pack-*` for sound pack releases
