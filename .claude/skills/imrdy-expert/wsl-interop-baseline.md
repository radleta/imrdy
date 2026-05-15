---
tags: [imrdy-expert/wsl]
summary: "WSL→Windows PATH passthrough varies per distro; explicit verification needed"
---

## WSL→Windows imrdy.exe Resolution: PATH Passthrough Works in 22.04 by Default

When invoked from inside Ubuntu-22.04 WSL, `which imrdy.exe` resolves to `/mnt/c/Users/radle/.local/bin/imrdy.exe` — the Windows-installed binary, surfaced through the default WSL PATH passthrough (`appendWindowsPath=true` in `/etc/wsl.conf`). Bare `imrdy hook` (no `.exe`) also resolves because WSL's binfmt_misc handler invokes `*.exe` via `/init`.

**However**, in Ubuntu-24.04 on the same machine, `which imrdy` did NOT resolve. Behavior is per-distro, not global — either passthrough is disabled in that distro's wsl.conf, the user's shell prepends a Linux PATH that hides it, or the file isn't reachable because of a stricter mount.

### Workaround

A "just install imrdy.exe on Windows and rely on WSL passthrough" recipe will silently fail on some distros. Any WSL onboarding doc must either:

1. **Verify PATH inside the target distro** before declaring success
2. **Ship a Linux-side wrapper** that explicitly execs `/mnt/c/Users/<user>/.local/bin/imrdy.exe`

This prevents silent failures during WSL distro setup.
