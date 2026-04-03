using Imrdy.Core.Sound;
using Imrdy.Windows.Models;
using Microsoft.Extensions.Logging;

namespace Imrdy.Windows.Menus;

/// <summary>
/// Builds the controller tray icon context menu.
/// Rebuilt dynamically on each Opening event from in-memory state.
/// </summary>
internal static class ControllerMenuBuilder
{
    public static ContextMenuStrip Create(
        Func<ControllerMenuState> stateProvider,
        Action<SoundConfig> onConfigChanged,
        ILogger? logger = null)
    {
        var menu = new ContextMenuStrip();
        menu.Opening += (_, _) =>
        {
            try
            {
                Rebuild(menu, stateProvider(), onConfigChanged, logger);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Error rebuilding controller menu");
            }
        };
        return menu;
    }

    private static void Rebuild(
        ContextMenuStrip menu,
        ControllerMenuState state,
        Action<SoundConfig> onConfigChanged,
        ILogger? logger)
    {
        menu.Items.Clear();

        // Sound toggle
        var soundToggle = new ToolStripMenuItem("Sounds")
        {
            CheckOnClick = true,
            Checked = state.Config.SoundEnabled,
        };
        soundToggle.Click += async (_, _) =>
        {
            try
            {
                var newConfig = state.Config with { SoundEnabled = soundToggle.Checked };
                await Task.Run(() => SoundConfigWriter.Save(newConfig, Path.Combine(state.SoundsDir, "config.json")));
                onConfigChanged(newConfig);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to save sound toggle");
            }
        };
        menu.Items.Add(soundToggle);

        // Sound Pack submenu
        var packMenu = new ToolStripMenuItem("Sound Pack");
        foreach (var pack in state.InstalledPacks)
        {
            var packName = pack;
            var item = new ToolStripMenuItem(packName)
            {
                Checked = string.Equals(packName, state.Config.Default, StringComparison.OrdinalIgnoreCase),
            };
            item.Click += async (_, _) =>
            {
                try
                {
                    var newConfig = state.Config with { Default = packName };
                    await Task.Run(() => SoundConfigWriter.Save(newConfig, Path.Combine(state.SoundsDir, "config.json")));
                    onConfigChanged(newConfig);
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Failed to save pack switch to {Pack}", packName);
                }
            };
            packMenu.DropDownItems.Add(item);
        }

        if (packMenu.DropDownItems.Count == 0)
        {
            packMenu.DropDownItems.Add(new ToolStripMenuItem("(none installed)") { Enabled = false });
        }

        menu.Items.Add(packMenu);

        menu.Items.Add(new ToolStripSeparator());

        // Sessions submenu
        var sessionsMenu = new ToolStripMenuItem($"Sessions ({state.Sessions.Count})");
        foreach (var session in state.Sessions)
        {
            var item = new ToolStripMenuItem($"{session.State.Project} [{session.State.Status}]")
            {
                Enabled = false,
            };
            sessionsMenu.DropDownItems.Add(item);
        }

        if (sessionsMenu.DropDownItems.Count == 0)
        {
            sessionsMenu.DropDownItems.Add(new ToolStripMenuItem("(no active sessions)") { Enabled = false });
        }

        menu.Items.Add(sessionsMenu);

        // Workspaces submenu
        var workspacesMenu = new ToolStripMenuItem("Workspaces");
        foreach (var ws in state.Workspaces)
        {
            var item = new ToolStripMenuItem(ws.Workspace.Name)
            {
                Enabled = false,
            };
            workspacesMenu.DropDownItems.Add(item);
        }

        if (workspacesMenu.DropDownItems.Count == 0)
        {
            workspacesMenu.DropDownItems.Add(new ToolStripMenuItem("(no workspaces)") { Enabled = false });
        }

        menu.Items.Add(workspacesMenu);

        menu.Items.Add(new ToolStripSeparator());

        // Open Config Folder
        menu.Items.Add("Open Config Folder", null, (_, _) =>
        {
            OpenFolder("explorer.exe", state.ConfigDir, logger);
        });

        // Open Sounds Folder
        menu.Items.Add("Open Sounds Folder", null, (_, _) =>
        {
            OpenFolder("explorer.exe", state.SoundsDir, logger);
        });

        // View Log
        menu.Items.Add("View Log", null, (_, _) =>
        {
            OpenFolder("explorer.exe", "/select," + state.LogPath, logger);
        });

        menu.Items.Add(new ToolStripSeparator());

        // Exit
        menu.Items.Add("Exit", null, (_, _) => state.OnExit());
    }

    private static void OpenFolder(string exe, string args, ILogger? logger)
    {
        System.Diagnostics.Process? proc = null;
        try
        {
            proc = System.Diagnostics.Process.Start(exe, args);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to open: {Exe} {Args}", exe, args);
        }
        finally
        {
            proc?.Dispose();
        }
    }
}
