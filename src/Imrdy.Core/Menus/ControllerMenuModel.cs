using System.Globalization;
using Imrdy.Core.Icons;

namespace Imrdy.Core.Menus;

internal static class ControllerMenuModel
{
    public static IReadOnlyList<MenuItemModel> Build(ControllerMenuState state)
    {
        var items = new List<MenuItemModel>
        {
            new() { Label = "imrdy", Enabled = false },
            new() { Type = MenuItemType.Separator },
        };

        items.Add(BuildSoundSubmenu(state));
        items.Add(BuildTraySubmenu(state));
        items.Add(BuildOverlaySubmenu(state));

        items.Add(new MenuItemModel { Type = MenuItemType.Separator });

        items.Add(BuildSessionItems(state));
        items.Add(BuildWorkspaceItems(state));

        items.Add(new MenuItemModel { Type = MenuItemType.Separator });

        items.Add(BuildManageSubmenu(state));

        items.Add(new MenuItemModel { Type = MenuItemType.Separator });

        items.Add(new MenuItemModel { Label = "Exit", Tag = "exit" });

        return items;
    }

    private static MenuItemModel BuildSoundSubmenu(ControllerMenuState state)
    {
        var children = new List<MenuItemModel>
        {
            new MenuItemModel
            {
                Label = "Sounds",
                Tag = "toggle-sound",
                Checked = state.Config.Sound.Enabled,
            },
            BuildSoundPackSubmenu(state),
            BuildEnabledPacksSubmenu(state),
        };

        return new MenuItemModel
        {
            Label = "Sound",
            Type = MenuItemType.Submenu,
            Children = children,
        };
    }

    private static MenuItemModel BuildTraySubmenu(ControllerMenuState state)
    {
        var children = new List<MenuItemModel>
        {
            new MenuItemModel
            {
                Label = "Enabled",
                Tag = "toggle-tray",
                Checked = state.Config.Tray.Enabled,
            },
            new MenuItemModel { Type = MenuItemType.Separator },
            BuildIconStyleSubmenu(state),
        };

        return new MenuItemModel
        {
            Label = "Tray",
            Type = MenuItemType.Submenu,
            Children = children,
        };
    }

    private static MenuItemModel BuildOverlaySubmenu(ControllerMenuState state)
    {
        var overlay = state.Config.Overlay;
        var children = new List<MenuItemModel>
        {
            new MenuItemModel
            {
                Label = "Enabled",
                Tag = "toggle-overlay",
                Checked = overlay.Enabled,
            },
            new MenuItemModel
            {
                Label = "Interactive",
                Tag = "toggle-overlay-interactive",
                Checked = overlay.Interactive ?? true,
            },
            new MenuItemModel { Type = MenuItemType.Separator },
            new MenuItemModel
            {
                Label = "Bottom Right",
                Tag = "set-overlay-position:bottom-right",
                Checked = string.Equals(overlay.Position, "bottom-right", StringComparison.OrdinalIgnoreCase),
            },
            new MenuItemModel
            {
                Label = "Bottom Left",
                Tag = "set-overlay-position:bottom-left",
                Checked = string.Equals(overlay.Position, "bottom-left", StringComparison.OrdinalIgnoreCase),
            },
            new MenuItemModel { Type = MenuItemType.Separator },
            new MenuItemModel
            {
                Label = "Small (48px)",
                Tag = "set-overlay-size:48",
                Checked = overlay.Size == 48,
            },
            new MenuItemModel
            {
                Label = "Medium (64px)",
                Tag = "set-overlay-size:64",
                Checked = overlay.Size == 64,
            },
            new MenuItemModel
            {
                Label = "Large (96px)",
                Tag = "set-overlay-size:96",
                Checked = overlay.Size == 96,
            },
            new MenuItemModel
            {
                Label = "Extra Large (128px)",
                Tag = "set-overlay-size:128",
                Checked = overlay.Size == 128,
            },
        };

        return new MenuItemModel
        {
            Label = "Overlay",
            Type = MenuItemType.Submenu,
            Children = children,
        };
    }

    private static MenuItemModel BuildSessionItems(ControllerMenuState state)
    {
        var children = new List<MenuItemModel>();

        var sorted = state.Sessions
            .OrderBy(s => s.DesktopIndex.HasValue ? 0 : 1)
            .ThenBy(s => s.DesktopIndex ?? 0);

        foreach (var s in sorted)
        {
            children.Add(new MenuItemModel
            {
                Label = $"{s.Project ?? s.SessionId} [{s.Status}]",
                Tag = $"switch-session:{s.SessionId}",
                Enabled = true,
            });
        }

        if (children.Count == 0)
        {
            children.Add(new MenuItemModel { Label = "(no active sessions)", Enabled = false });
        }

        return new MenuItemModel
        {
            Label = $"Sessions ({state.Sessions.Count})",
            Type = MenuItemType.Submenu,
            Children = children,
        };
    }

    private static MenuItemModel BuildWorkspaceItems(ControllerMenuState state)
    {
        var children = new List<MenuItemModel>();

        var sorted = state.Workspaces
            .OrderBy(ws => ws.DesktopIndex);

        foreach (var ws in sorted)
        {
            children.Add(new MenuItemModel
            {
                Label = ws.WorkspaceName,
                Tag = $"switch-workspace:{ws.WorkspacePath}",
                Enabled = true,
            });
        }

        if (children.Count == 0)
        {
            children.Add(new MenuItemModel { Label = "(no workspaces)", Enabled = false });
        }

        return new MenuItemModel
        {
            Label = "Workspaces",
            Type = MenuItemType.Submenu,
            Children = children,
        };
    }

    private static MenuItemModel BuildManageSubmenu(ControllerMenuState state)
    {
        var children = new List<MenuItemModel>();

        if (state.Wsl is { } wsl)
        {
            children.Add(BuildWslSubmenu(wsl));
            children.Add(new MenuItemModel { Type = MenuItemType.Separator });
        }

        children.Add(new MenuItemModel { Label = "Open Config Folder", Tag = "open-config" });
        children.Add(new MenuItemModel { Label = "View Log", Tag = "open-log" });

        if (state.DevBuild is { } dev)
        {
            children.Add(new MenuItemModel { Type = MenuItemType.Separator });
            children.Add(BuildDevSubmenu(dev));
        }

        return new MenuItemModel
        {
            Label = "Manage",
            Type = MenuItemType.Submenu,
            Children = children,
        };
    }

    private static MenuItemModel BuildWslSubmenu(WslMenuState state)
    {
        var children = new List<MenuItemModel>
        {
            new MenuItemModel
            {
                Label = "Watch All",
                Tag = "toggle-wsl-watch-all",
                Checked = state.WatchAll,
            },
        };

        if (state.Distros.Count > 0)
        {
            children.Add(new MenuItemModel { Type = MenuItemType.Separator });

            foreach (var entry in state.Distros)
            {
                var label = entry.IsRunning
                    ? $"{entry.Name}   (running · {entry.SessionCount} {(entry.SessionCount == 1 ? "session" : "sessions")})"
                    : $"{entry.Name}   (stopped)";

                children.Add(new MenuItemModel
                {
                    Label = label,
                    Tag = $"toggle-wsl-distro:{entry.Name}",
                    Checked = entry.Enabled,
                    Enabled = state.WatchAll,
                });
            }
        }

        children.Add(new MenuItemModel { Type = MenuItemType.Separator });
        children.Add(new MenuItemModel { Label = "Rescan Distros", Tag = "rescan-distros" });
        children.Add(new MenuItemModel { Label = "Open WSL Config", Tag = "open-wsl-config" });
        children.Add(new MenuItemModel { Label = "View WSL Log", Tag = "view-wsl-log" });

        return new MenuItemModel
        {
            Label = "WSL",
            Type = MenuItemType.Submenu,
            Children = children,
        };
    }

    private static MenuItemModel BuildDevSubmenu(DevBuildState dev)
    {
        var children = new List<MenuItemModel>();

        if (dev.Fixtures.Count == 0)
        {
            children.Add(new MenuItemModel { Label = "(no fixtures found)", Enabled = false });
        }
        else
        {
            foreach (var fixture in dev.Fixtures)
            {
                children.Add(new MenuItemModel
                {
                    Label = fixture.DisplayName,
                    Tag = $"dev-preview:{fixture.FullPath}",
                });
            }
        }

        children.Add(new MenuItemModel { Type = MenuItemType.Separator });
        children.Add(new MenuItemModel
        {
            Label = dev.RunningPreviewCount > 0
                ? $"Close All ({dev.RunningPreviewCount})"
                : "Close All",
            Tag = "dev-preview-close-all",
            Enabled = dev.RunningPreviewCount > 0,
        });

        return new MenuItemModel
        {
            Label = "Dev",
            Type = MenuItemType.Submenu,
            Children = children,
        };
    }

    private static MenuItemModel BuildSoundPackSubmenu(ControllerMenuState state)
    {
        var children = new List<MenuItemModel>();
        var defaultPack = state.Config.Sound.DefaultPack;
        var isRandom = string.Equals(defaultPack, "random", StringComparison.OrdinalIgnoreCase);
        var isNone = string.IsNullOrEmpty(defaultPack);

        // Random option
        children.Add(new MenuItemModel
        {
            Label = "Random",
            Tag = "switch-pack:random",
            Checked = isRandom,
        });

        // Individual packs
        foreach (var pack in state.InstalledPacks)
        {
            children.Add(new MenuItemModel
            {
                Label = pack,
                Tag = $"switch-pack:{pack}",
                Checked = !isRandom && !isNone
                    && string.Equals(pack, defaultPack, StringComparison.OrdinalIgnoreCase),
            });
        }

        if (state.InstalledPacks.Count > 0)
        {
            children.Add(new MenuItemModel { Type = MenuItemType.Separator });
        }

        // None option
        children.Add(new MenuItemModel
        {
            Label = "(None)",
            Tag = "switch-pack:",
            Checked = isNone,
        });

        return new MenuItemModel
        {
            Label = "Sound Pack",
            Type = MenuItemType.Submenu,
            Children = children,
        };
    }

    private static MenuItemModel BuildIconStyleSubmenu(ControllerMenuState state)
    {
        var children = new List<MenuItemModel>();
        var currentStyle = StyleNames.NormalizeStyleName(state.Config.Tray.IconStyle) ?? "circles";
        var textInfo = CultureInfo.InvariantCulture.TextInfo;

        // Built-in shape styles
        foreach (var styleName in StyleNames.BuiltInStyles)
        {
            children.Add(new MenuItemModel
            {
                Label = textInfo.ToTitleCase(styleName),
                Tag = $"switch-icon-style:{styleName}",
                Checked = string.Equals(currentStyle, styleName, StringComparison.OrdinalIgnoreCase),
            });
        }

        // Installed graphics packs (after separator)
        if (state.InstalledGraphicsPacks.Count > 0)
        {
            children.Add(new MenuItemModel { Type = MenuItemType.Separator });

            foreach (var pack in state.InstalledGraphicsPacks)
            {
                var packStyle = $"pack:{pack}";
                children.Add(new MenuItemModel
                {
                    Label = pack,
                    Tag = $"switch-icon-style:{packStyle}",
                    Checked = string.Equals(currentStyle, packStyle, StringComparison.OrdinalIgnoreCase),
                });
            }
        }

        return new MenuItemModel
        {
            Label = "Icon Style",
            Type = MenuItemType.Submenu,
            Children = children,
        };
    }

    private static MenuItemModel BuildEnabledPacksSubmenu(ControllerMenuState state)
    {
        var children = new List<MenuItemModel>();
        var disabled = state.Config.Sound.DisabledPacks;

        if (state.InstalledPacks.Count == 0)
        {
            children.Add(new MenuItemModel
            {
                Label = "(none installed)",
                Enabled = false,
            });
        }
        else
        {
            foreach (var pack in state.InstalledPacks)
            {
                var isDisabled = disabled.Any(d =>
                    string.Equals(d, pack, StringComparison.OrdinalIgnoreCase));
                children.Add(new MenuItemModel
                {
                    Label = pack,
                    Tag = $"toggle-pack-enabled:{pack}",
                    Checked = !isDisabled,
                });
            }
        }

        return new MenuItemModel
        {
            Label = "Enabled Packs",
            Type = MenuItemType.Submenu,
            Children = children,
        };
    }
}
