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

The plugin auto-installs the binary and default sound pack on first session start via the bootstrap script.

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

Sound packs live in `~/.claude/sounds/packs/<pack-name>/`. Each pack has a `pack.json` manifest and event folders containing `.wav` files.

**Events:** GettingToWork, Finished, SessionEnd, NeedsYou, Forgotten

**Pack structure:**
```
~/.claude/sounds/packs/my-pack/
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

Or map packs to specific projects in `~/.claude/sounds/config.json`:
```json
{
  "default": "assistant",
  "projectMappings": {
    "my-project": "retro"
  }
}
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
| Session state files | `~/.imrdy/sessions/*.json` |
| Workspace config | `~/.imrdy/workspaces.json` |
| Sound config | `~/.claude/sounds/config.json` |
| Sound packs | `~/.claude/sounds/packs/` |
| Logs | `~/.imrdy/logs/monitor.log` |

Run `imrdy config path` to see full paths on your system.

## Building from Source

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (preview).

```bash
dotnet build
dotnet test
dotnet publish src/Imrdy.Windows/Imrdy.Windows.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

## Architecture

- **Imrdy.Core** — Platform-independent: state files, sound system, workspace management, validation, DI
- **Imrdy.Windows** — WinForms tray app, COM virtual desktop interop, CLI commands, hook command

Single executable via PublishSingleFile + SelfContained (not AOT — WinForms/COM compatibility).

## License

MIT
