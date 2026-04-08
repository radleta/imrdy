# imrdy

Windows system tray monitor for Claude Code sessions. .NET 10, WinForms, single executable.

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

## Build & Test

```bash
dotnet build                                    # Debug build
dotnet test --filter "Category!=Integration&Category!=Benchmark"  # Unit tests only (291 tests)
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
