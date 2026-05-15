---
tags: [imrdy-expert/wsl]
summary: "WSL_DISTRO_NAME env var requires explicit pickup via IHookEnvironment; code exists but fallback was never wired"
---

## WSL_DISTRO_NAME Env Var Requires Explicit IHookEnvironment Pickup

The Phase 1 `wsl_distro` field was fully plumbed into state files and hook models, but **no code reads `Environment.GetEnvironmentVariable("WSL_DISTRO_NAME")`** to populate it. Real `imrdy hook` invocations inside a WSL distro produce `wsl_distro: null` in the state file, even though the env var is set.

### The Gotcha

`HookCommand.cs:192` reads `WslDistro` only from the stdin JSON:

```csharp
WslDistro = hookEvent.WslDistro
```

Claude Code (the Windows parent process) never writes `WslDistro` into the hook event JSON payload. The Linux binary has no other way to inject the distro identity. **Solution**: Use `IHookEnvironment` as an abstraction layer so the hook command can fall back to the platform-specific implementation when the JSON field is null.

### Platform-Specific Behavior

| Platform | Env Var | Implementation |
|----------|---------|---|
| **Windows** | N/A (always null) | `WindowsHookEnvironment.GetWslDistro()` returns `null` |
| **Linux/WSL** | `WSL_DISTRO_NAME` set in every process | `LinuxHookEnvironment.GetWslDistro()` returns the env var value or `null` if unset |

### Fix (Applied in Step 06b)

1. Add `string? GetWslDistro()` method to `IHookEnvironment` interface
2. Windows impl returns `null`
3. Linux impl returns `Environment.GetEnvironmentVariable("WSL_DISTRO_NAME")`
4. In `HookCommand.cs:192`, change to: `WslDistro = hookEvent.WslDistro ?? hookEnvironment.GetWslDistro()`
5. Add unit tests covering both env-var-set and env-var-unset cases

### Impact

Downstream features in Phase 2+ depend on `wsl_distro` being populated from real Linux hook events:
- **WslDistroDiscovery** filter in tray
- Dashboard distro chip (Step 11)
- Multi-watcher arm/disarm logic (Step 12)

Without this wiring, the tray cannot distinguish WSL-originated sessions from Windows-native ones.

### Discovery

Empirical validation during Milestone A end-to-end testing revealed the field was always null from real Linux hook invocations, despite being correctly defined in the schema. The fix was added mid-Phase-1 as step 06b (hot-fix outside the original 1-16 numbering).
