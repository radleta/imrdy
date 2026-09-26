---
tags: [imrdy-expert/wsl]
summary: "WSL and imrdy: Windows PATH passthrough varies per distro, a Windows imrdy.exe run from WSL cannot see WSL_DISTRO_NAME, and wsl_distro comes only from the Linux hook's IHookEnvironment — carried on the state file but never rendered"
last-verified: "2026-09-25"
---

# WSL Interop

## Where `wsl_distro` comes from

Claude Code does not put a distro name in the hook payload. `HookCommand` fills
`StateFileModel.WslDistro` as `hookEvent.WslDistro ?? hookEnvironment.GetWslDistro()`, and only
the Linux binary answers: `LinuxHookEnvironment.GetWslDistro()` returns the `WSL_DISTRO_NAME`
environment variable, `WindowsHookEnvironment.GetWslDistro()` returns `null`. So a session gets a
distro only when the Linux `imrdy hook` runs inside the distro. `PreserveFields` then carries the
value across events that do not set it (see [Field Preservation Catalog](field-preservation-catalog.md)).

The field is carried, not displayed. The dashboard's machine chip renders
`DashboardViewModel.OriginMachine`; `WslDistro` rides on the view model unrendered, so one box never
shows two labels. A session with no `origin_machine` shows no machine label, whatever its `wsl_distro`. Anything
that needs to tell a WSL session from a native one — or one distro from another — keys on
`origin_machine` (`<hostname>-<distro>` by default, via `MachineNameResolver`), not on
`wsl_distro`.

## A Windows binary launched from WSL cannot see the distro

A Windows-native `imrdy.exe` started from inside WSL does NOT inherit `WSL_DISTRO_NAME`. WSLENV
forwards only the variables it lists — observed on Ubuntu-22.04 as
`TERM:COLORTERM:TERM_PROGRAM:TERM_PROGRAM_VERSION` — and distro identity is not among them.
`Environment.GetEnvironmentVariable("WSL_DISTRO_NAME")` in the Windows process sees whatever
Windows set, typically nothing. A distro name has to be carried across the boundary explicitly:
the Linux binary, a shim that adds it to the payload, or `WSL_DISTRO_NAME` appended to WSLENV in
the distro.

## Windows PATH passthrough is per distro

From Ubuntu-22.04, `which imrdy.exe` resolved to the Windows install under
`/mnt/c/Users/<user>/.local/bin/`, through the default `appendWindowsPath=true`, and bare `imrdy`
resolved via binfmt_misc. On Ubuntu-24.04 on the same machine it did not resolve. The cause can be
the distro's `wsl.conf`, a shell rc that rebuilds `PATH`, or the mount — so any recipe that relies
on WSL reaching the Windows `imrdy.exe` must check `PATH` inside the target distro before declaring
success, or exec the Windows binary by absolute path.
