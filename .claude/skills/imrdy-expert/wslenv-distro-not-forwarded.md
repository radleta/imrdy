---
tags: [imrdy/wsl-interop]
updated: 2026-04-28
summary: "WSLENV doesn't auto-forward WSL_DISTRO_NAME; Windows binaries can't self-identify source distro"
---

## WSLENV Doesn't Auto-Forward WSL_DISTRO_NAME

A Windows-native binary launched from inside WSL (e.g., `imrdy.exe hook` called by Claude Code in Ubuntu-22.04) does NOT inherit `WSL_DISTRO_NAME` in its process environment. The WSLENV protocol explicitly opts variables into cross-boundary forwarding — on this machine, 22.04's WSLENV is `TERM:COLORTERM:TERM_PROGRAM:TERM_PROGRAM_VERSION` — distro identity is not on the list.

So `imrdy.exe` cannot tell which WSL distro a hook came from by reading its own `Environment.GetEnvironmentVariable("WSL_DISTRO_NAME")`. It will see whatever Windows had set (typically empty).

### Impact

Any feature needing per-distro behavior (per-distro display badge, per-distro icon, per-distro click action like `wsl.exe -d Ubuntu-22.04 ...`) cannot rely on env forwarding alone.

### Workarounds

1. **Add WSL_DISTRO_NAME to WSLENV** in each distro (`/etc/profile.d/` or user shell rc)
2. **Wrap `imrdy hook` in a bash shim** that injects distro identity into the JSON payload or CLI flag before piping to `imrdy.exe`
3. **Pass through Claude Code's hook context** if the harness supports environment forwarding

The producer side (WSL) needs a wrapper to carry distro identity through the process boundary.
