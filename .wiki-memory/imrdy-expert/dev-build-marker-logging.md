---
tags: [imrdy/logging, dev-build, diagnostic]
updated: 2026-04-24
summary: "Touch ~/.imrdy/.dev-build marker after dev deploys to enable Debug logging on all imrdy processes; enables diagnostic traces without env var friction"
---

# Dev Build Marker & Logging

## The Pattern

`build-dev.sh` touches `~/.imrdy/.dev-build` after every `publish → stop → deploy → respawn` cycle. `ServiceRegistration.AddSerilog` checks for the marker file and promotes the minimum log level from **Information** to **Debug**. This ensures all debug diagnostic logging is visible in dev environments without requiring developers to remember `IMRDY_LOG=1`.

## Why Not Just Use Env Vars?

Environment variables don't survive across process boundaries in the imrdy launch chain:

1. `build-dev.sh` sets `IMRDY_LOG=1`
2. `build-dev.sh` invokes `imrdy stop` (kills the tray)
3. `HookCommand` auto-spawns a fresh tray process
4. **The new tray process inherits a scrubbed environment** (no `IMRDY_LOG=1`)
5. Debug logging is now invisible, even though the deploy succeeded

File-based markers survive across all process boundaries:

- `~/.imrdy/.dev-build` lives on disk, not in process state
- Every `imrdy` process (hook, CLI, tray, auto-spawn) reads it at startup
- Every child process created by the auto-spawn sees it
- Marker persists across `build-dev.sh` respawns

## Implementation

### In `ServiceRegistration.AddSerilog`

```csharp
public static IServiceCollection AddSerilog(this IServiceCollection services, string logPath)
{
    var isDebug = Environment.GetEnvironmentVariable("IMRDY_LOG") == "1"
        || File.Exists(ImrdyPaths.DevBuildMarker);

    var minLevel = isDebug ? LogEventLevel.Debug : LogEventLevel.Information;

    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Is(minLevel)
        .WriteTo.File(
            logPath,
            fileSizeLimitBytes: 1024 * 1024,
            retainedFileCountLimit: 5,
            shared: true,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
        )
        .CreateLogger();

    services.AddLogging(b => b.AddSerilog());
    return services;
}
```

### In `ImrdyPaths`

```csharp
public static string DevBuildMarker =>
    Path.Combine(Home, ".imrdy", ".dev-build");
```

The marker is **always an empty file** (just its presence is the signal). No content, no modification time check.

## Lifecycle

**Dev deploy (build-dev.sh):**
```bash
dotnet publish ...
touch ~/.imrdy/.dev-build         # ← Enable Debug logging
imrdy stop
# Kill and respawn happen here
# All processes pick up Debug logging
```

**Clean publish (no build-dev.sh):**
```bash
dotnet publish ...
# Marker is NOT created
# Info-level logging by default
```

**Disable Debug logging during local testing:**
```bash
rm ~/.imrdy/.dev-build
# Next imrdy process uses Info level only
# (even if started from a prior build-dev.sh deploy)
```

## Observable Effects

**When marker is present:**

```log
2026-04-24 14:23:45.123 [DBG] Drain tick: Sessions=2, Overlay visible=false, Dwell ticks=0
2026-04-24 14:23:45.125 [DBG] OverlayWindow updated: Style=circles, Items=2
2026-04-24 14:23:45.150 [DBG] HookCommand stdin: {"session_id":"abc123","status":"busy",...}
2026-04-24 14:23:45.151 [DBG] Session abc123 → busy (UserPromptSubmitted)
```

Debug messages appear for:
- Drain tick iterations (every 100ms)
- Overlay rendering state
- Hook event payloads (JSON)
- State machine transitions

**When marker is absent:**

```log
2026-04-24 14:23:45.200 [INF] Tray started (3 sessions loaded)
2026-04-24 14:23:46.500 [WRN] File watcher event throttled
```

Only Info and above appear. Debug traces are completely silent.

## Use Cases

### Platform Boundary Diagnostics

When debugging COM interop, P/Invoke, or window handle issues, enable Debug logging to capture vtable dispatch, HRESULT values, and coordinate transforms:

```log
[DBG] ComVirtualDesktop: GetCurrentDesktopIndex returned 2
[DBG] ComVirtualDesktop: GetApplicationViewForHwnd(0x5a0e2e): hr=0x0
[DBG] ComVirtualDesktop: IsViewPinned check returned 0 (not pinned)
[DBG] ComVirtualDesktop: PinView dispatch starting
[DBG] PInvokeOverlay: ScreenToClientPoint(0xd0ce5e) @ screen(1920,1200) → client(43,45)
```

### State Machine Tracing

When hover behavior seems wrong (form appears/disappears unexpectedly), Debug logs show every transition:

```log
[DBG] HoverController: Dwell ticks = 3, threshold = 2, showing form
[DBG] HoverController: TryShowForm @ Y=450, form height=200, screen working area Y=0..1080
[DBG] HoverController: Form bounds = Rectangle(200, 300, 400, 200)
[DBG] HoverController: Cursor in corridor? (x=300, y=320) → true
[DBG] HoverController: Cursor in corridor? (x=150, y=150) → false, corridor expiry countdown
```

## Gotcha: Durable Marker

The marker is **not automatically cleaned up**. After a dev deploy, subsequent clean publishes (via `dotnet publish` directly, not `build-dev.sh`) will not recreate it, but Debug logging remains enabled.

**This is intentional** — developers often iterate locally with `build-dev.sh`, then run a production-like test, then go back to dev work. Having to manually recreate the marker would be friction.

To disable it after finishing a dev session:

```bash
rm ~/.imrdy/.dev-build
```

If you want a script to auto-clean on fresh publishes, use a `clean-build.sh` variant that removes the marker before publishing.

## Related

- [Platform Boundary Three-Seal Gate](../../../verify-fix-loop-expert/.wiki-memory/verify-fix-loop-expert/platform-boundary-three-seal-gate.md) (user-scoped wiki) — Diagnostic logs are the third seal for platform boundary verification
