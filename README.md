# imrdy

System tray monitor for Claude Code sessions on Windows. Replaces the PowerShell + Node.js dual-runtime architecture with a single .NET executable.

## Why

Managing multiple Claude Code sessions in parallel is an attention problem: knowing which session needs you, which is working, which is idle, and acting on the right one without losing focus on your work. imrdy puts that information in the system tray where it stays glanceable in peripheral vision and never demands foreground attention.

## How it looks

Each active Claude Code session gets a colored circle icon in the system tray:
- **Red** = busy (working)
- **Teal** = idle, but background agents are still running (waiting for you; silent, no toast — may resume itself)
- **Green** = idle, nothing running (genuinely waiting for your input)
- **Orange** = needs attention
- **Purple** = permission requested (elicitation dialog)
- **Yellow** = error (tool or stop failure)
- Icons age (darken) over time based on last interaction

Click a session icon to switch to its virtual desktop and focus the terminal window.

## Installation

### Plugin (recommended)

```bash
claude plugin add https://github.com/radleta/imrdy
```

The plugin auto-installs the binary and default sound pack (`assistant`) on first session start via the bootstrap script. The sound pack is downloaded from the latest `pack-assistant-*` GitHub Release. The default config uses `"random"` pack selection, which picks randomly from enabled installed packs.

### Manual

**PowerShell (one-liner):**
```powershell
irm https://raw.githubusercontent.com/radleta/imrdy/main/install.ps1 | iex
```

**Or download from [Releases](https://github.com/radleta/imrdy/releases)** and place `imrdy.exe` in a directory on your PATH (e.g., `~/.local/bin/`).

## How It Works

1. **Hook** (`imrdy hook`): Called by Claude Code on every session event (start, prompt, tool use, stop, etc.). Reads hook JSON from stdin, writes a session state file to `~/.imrdy/sessions/`.

2. **Monitor** (`imrdy` with no args): WinForms system tray app that watches session state files via FileSystemWatcher. Creates/updates/removes tray icons as sessions change.

3. **CLI** (`imrdy status|packs|config|workspace|inspect-live|render-live`): Management commands for checking status, managing sound packs, editing config, pinning workspaces, and live diagnostic inspection of the running tray.

The tray monitor auto-starts on the first hook event (mutex-gated — only one instance runs). To disable auto-start:
```bash
imrdy config set tray.enabled false
```

## CLI Commands

```
imrdy status              Show active sessions and workspaces
imrdy status --json       Machine-readable output

imrdy packs list          List installed sound packs
imrdy packs test <name>   Play a random sound from a pack
imrdy packs validate      Validate pack structure
imrdy packs set-default   Set the default sound pack
imrdy packs remove <name> Remove an installed pack
imrdy packs pack <path>   Validate and package a pack as ZIP

imrdy config show         Show current configuration
imrdy config set <k> <v>  Update a config value
imrdy config path         Show all file paths
imrdy config validate     Validate config and workspace files

imrdy workspace list      List pinned workspaces
imrdy workspace pin <p>   Pin a workspace (auto-derives name)
imrdy workspace unpin <p> Unpin a workspace

imrdy stop                Stop the tray app (auto-restarts on next hook)

imrdy inspect-live <id>   Walk the live SessionDashboardForm for <id> and emit control-tree JSON + diagnostics
imrdy render-live <id>    Capture a live SessionDashboardForm PNG for <id> via the tray IPC server

imrdy --help              Show help
imrdy --version           Show version
```

All commands support `--json` for machine consumption.

## Sound Packs

Sound packs live in `~/.imrdy/sounds/packs/<pack-name>/`. Each pack has a `pack.json` manifest and event folders containing `.wav` files.

**Events:** GettingToWork, Finished, SessionEnd, NeedsYou, Forgotten

**Pack structure:**
```
~/.imrdy/sounds/packs/my-pack/
  pack.json
  getting-to-work/
    clip1.wav
    clip2.wav
  finished/
    clip1.wav
  ...
```

Set the default pack:
```bash
imrdy packs set-default my-pack
```

### Authoring Sound Packs

To create and distribute a custom sound pack:

1. Create a pack directory under `sounds/` in the repo (e.g., `sounds/my-pack/`) with the standard structure above
2. Validate and package it:
   ```bash
   imrdy packs pack sounds/my-pack/ --output ./dist/
   ```
3. Tag a release: `git tag pack-my-pack-v1.0.0 && git push --tags`
4. The `release-packs.yml` workflow builds the ZIP and creates a GitHub Release with the artifact and SHA256 checksum

Or map packs to specific projects via config:
```bash
imrdy config set sound.defaultPack my-pack
```

## Graphics Packs

Graphics packs let you replace the default colored-dot tray icons with custom SVG artwork. Each session icon is rendered by a pack at runtime via Svg.NET.

**Pack location:** `~/.imrdy/graphics/packs/<pack-name>/`

**Pack structure:**
```
~/.imrdy/graphics/packs/my-pack/
  pack.json
  idle.svg
  busy.svg
  attention.svg
  permission.svg
  compact.svg
  unknown.svg
  workspace.svg
```

**Minimal `pack.json`:**
```json
{
  "name": "my-pack",
  "format": "svg",
  "version": "1.0.0",
  "license": "MIT",
  "states": {
    "idle":       { "file": "idle.svg" },
    "busy":       { "file": "busy.svg" },
    "attention":  { "file": "attention.svg" },
    "permission": { "file": "permission.svg" },
    "compact":    { "file": "compact.svg" },
    "unknown":    { "file": "unknown.svg" },
    "workspace":  { "file": "workspace.svg" }
  }
}
```

**Install a pack:** Drop the pack folder into `~/.imrdy/graphics/packs/`. Only install packs from trusted sources — SVG files are rendered at runtime and pack content is not sanitized in this release.

**Switch to a pack:**
```bash
imrdy config set tray.iconStyle pack:my-pack
```

**Switch back to dots:**
```bash
imrdy config set tray.iconStyle dots
```

Aging (session idle time) is applied automatically to all packs via `ColorMatrix` desaturation and dimming — no pack-specific work required. If a pack fails to load, the tray silently falls back to the built-in dot renderer.

All packs must declare a `license` field in `pack.json`. A stub `dev-test` pack ships with the source under `src/Imrdy.Windows/Resources/graphics-packs/` for use in development.

## Overlay (Mode B)

An alternative to the 16px tray icons: a floating borderless window that renders session characters as a horizontal row. Uses the active graphics pack (or colored circles in dots mode). Stays on top via `Form.TopMost = true`, and never steals focus from your terminal.

The overlay free-floats: drag it by the grip handle on its left edge (six dots, dimmed until you hover it) and drop it anywhere. Release within ~24px of a screen edge or corner and it snaps flush; drop it further in and it stays where you put it. The position is remembered per monitor.

**Enable:**
```bash
imrdy config set overlay.enabled true
```

**Config fields:**

| Field | Default | Description |
|-------|---------|-------------|
| `overlay.enabled` | `false` | Show the overlay window |
| `overlay.position` | `"bottom-right"` | Fallback anchor used when no offset is set: `top-left`, `top-center`, `top-right`, `bottom-left`, `bottom-center`, `bottom-right` |
| `overlay.size` | `64` | Icon size in pixels (32–256) |
| `overlay.spacing` | `8` | Gap between icons in pixels (0–32) |
| `overlay.monitor` | `0` | Monitor index to dock to (0 = primary) |
| `overlay.locked` | `false` | Prevent repositioning by dragging the grip |
| `overlay.offsetX` | `null` | Free-float X, in logical px from the target monitor's working-area origin. `null` falls back to `overlay.position` |
| `overlay.offsetY` | `null` | Free-float Y, same units. `null` falls back to `overlay.position` |

`overlay.offsetX` / `overlay.offsetY` are written for you when you drag the overlay or pick a position from its menu — you rarely need to set them by hand.

**CLI examples:**
```bash
imrdy config set overlay.position bottom-left
imrdy config set overlay.size 128
```

`imrdy config set` covers `overlay.enabled`, `overlay.position`, `overlay.size`, and `overlay.spacing`. Set `monitor`, `locked`, and the offsets from the overlay menu (below) or by editing `~/.imrdy/config.json`.

**Mouse:**

- **Drag the grip** (left edge) — reposition the overlay; snaps to a nearby edge or corner
- **Left-click an icon** — switch to that session's desktop and focus its terminal
- **Right-click an icon** — session or workspace menu
- **Right-click the empty area** — overlay settings menu: the 6 positions, spacing presets, monitor selector, and a **Lock** toggle

**Controller menu:** Right-click the controller icon → **Overlay** — the same settings menu: toggle enabled, position, size, spacing, monitor, and Lock.

**Limitations:** No animation, no peek mode.

## Controller Tray Icon

A persistent controller icon (headphones) appears in the system tray whenever the monitor is running. Right-click for a context menu:

- **Sounds** — Toggle sound playback on/off (checked = enabled)
- **Sound Pack** — Switch the active pack (Random, installed packs, or None)
- **Enabled Packs** — Toggle individual packs on/off for random selection
- **Icon Style** — Switch between dot icons and installed graphics packs (Dots, installed packs)
- **Overlay** — Toggle overlay window, select position, size, spacing, and monitor; lock its position
- **Sessions** — View and switch to active sessions
- **Workspaces** — View and switch to pinned workspaces
- **Open Config Folder / Open Sounds Folder / View Log** — Quick access to file locations
- **Exit** — Shut down the monitor

Sound can also be toggled via CLI:
```bash
imrdy config set sound.enabled false   # disable sounds
imrdy config set sound.enabled true    # enable sounds
```

## Virtual Desktops

imrdy integrates with Windows virtual desktops:
- Left-click a session icon to switch to its desktop and focus the terminal
- Toast notifications are suppressed for sessions on the current desktop
- Pin workspaces to specific desktops via `imrdy workspace pin <path> --desktop 2`

Supports Windows 10 (20H1+) and Windows 11 (all versions through 24H2).

## Configuration

**File paths:**
| File | Location |
|------|----------|
| Config | `~/.imrdy/config.json` |
| Session state files | `~/.imrdy/sessions/*.json` |
| Workspace config | `~/.imrdy/workspaces.json` |
| Sound packs | `~/.imrdy/sounds/packs/` |
| Graphics packs | `~/.imrdy/graphics/packs/` |
| Logs | `~/.imrdy/logs/monitor.log` |

**Config schema (`~/.imrdy/config.json`):**
```json
{
  "tray": { "enabled": true, "iconStyle": "dots" },
  "sound": { "enabled": true, "defaultPack": "random", "disabledPacks": [] },
  "overlay": { "enabled": false, "position": "bottom-right", "size": 64, "spacing": 8, "monitor": 0, "locked": false, "offsetX": null, "offsetY": null },
  "diagnostics": { "ipcEnabled": null }
}
```

`diagnostics.ipcEnabled` is a three-state `bool?`. `null` (default — omit from config) means the IPC server starts only when the `~/.imrdy/.dev-build` dev marker exists. Set `true` to enable in production; set `false` to disable even in dev.

**Environment variables:**
| Variable | Purpose |
|----------|---------|
| `IMRDY_HOME` | Override config/data directory (default: `~/.imrdy/`) |
| `IMRDY_NO_TRAY` | Set to `1` to suppress tray auto-spawn (headless CI, containers, SSH) |
| `IMRDY_LOG` | Set to `1` to enable debug logging |

Run `imrdy config path` to see full paths on your system.

## Building from Source

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (preview).

```bash
dotnet build
dotnet test
```

### Local Development

Build, deploy to `~/.local/bin/`, and restart the tray app in one step:

```bash
./build-dev.sh
```

This publishes the binary, copies it to `~/.local/bin/imrdy.exe`, and signals the running tray to stop. The next Claude Code hook event auto-spawns the updated binary.

For a publish-only build without local deploy:

```bash
dotnet publish src/Imrdy.Windows/Imrdy.Windows.csproj -c Release
```

## Architecture

- **Imrdy.Core** — Platform-independent: state files, sound system, workspace management, menu models (Build/Apply pattern), validation, DI
- **Imrdy.Windows** — WinForms tray app (session icons + controller icon), menu rendering, COM virtual desktop interop, CLI commands, hook command

Single executable via PublishSingleFile + SelfContained (no IL trimming — WinForms/COM incompatibility).

## License

MIT
