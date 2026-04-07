# imrdy

System tray monitor for Claude Code sessions on Windows. Replaces the PowerShell + Node.js dual-runtime architecture with a single .NET executable.

Each active Claude Code session gets a colored circle icon in the system tray:
- **Yellow** = busy (working)
- **Green** = idle (waiting for input)
- **Red** = needs attention (permission request, elicitation dialog)
- Icons age (darken) over time based on last interaction

Click a session icon to switch to its virtual desktop and focus the terminal window.

## Installation

### Plugin (recommended)

```bash
claude plugin add https://github.com/radleta/imrdy
```

The plugin auto-installs the binary and default sound pack (`assistant`) on first session start via the bootstrap script. The sound pack is downloaded from the latest `pack-assistant-*` GitHub Release.

### Manual

**PowerShell (one-liner):**
```powershell
irm https://raw.githubusercontent.com/radleta/imrdy/main/install.ps1 | iex
```

**Or download from [Releases](https://github.com/radleta/imrdy/releases)** and place `imrdy.exe` in a directory on your PATH (e.g., `~/.local/bin/`).

## How It Works

1. **Hook** (`imrdy hook`): Called by Claude Code on every session event (start, prompt, tool use, stop, etc.). Reads hook JSON from stdin, writes a session state file to `~/.imrdy/sessions/`.

2. **Monitor** (`imrdy` with no args): WinForms system tray app that watches session state files via FileSystemWatcher. Creates/updates/removes tray icons as sessions change.

3. **CLI** (`imrdy status|packs|config|workspace`): Management commands for checking status, managing sound packs, editing config, and pinning workspaces.

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

## Controller Tray Icon

A persistent controller icon (headphones) appears in the system tray whenever the monitor is running. Right-click for a context menu:

- **Sounds** — Toggle sound playback on/off (checked = enabled)
- **Sound Pack** — Switch the active pack from installed packs
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
- Balloon tip notifications are suppressed for sessions on the current desktop
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
| Logs | `~/.imrdy/logs/monitor.log` |

**Config schema (`~/.imrdy/config.json`):**
```json
{
  "tray": { "enabled": true },
  "sound": { "enabled": true, "defaultPack": "assistant" }
}
```

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
dotnet publish src/Imrdy.Windows/Imrdy.Windows.csproj -c Release -r win-x64
```

## Architecture

- **Imrdy.Core** — Platform-independent: state files, sound system, workspace management, menu models (Build/Apply pattern), validation, DI
- **Imrdy.Windows** — WinForms tray app (session icons + controller icon), menu rendering, COM virtual desktop interop, CLI commands, hook command

Single executable via PublishSingleFile + SelfContained (no IL trimming — WinForms/COM incompatibility).

## License

MIT
