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

        // Sound toggle
        items.Add(new MenuItemModel
        {
            Label = "Sounds",
            Checked = state.Config.Sound.Enabled,
            Tag = "toggle-sound",
        });

        // Sound Pack submenu (default selection: Random | packs... | None)
        items.Add(BuildSoundPackSubmenu(state));

        // Enabled Packs submenu (checkbox toggles)
        items.Add(BuildEnabledPacksSubmenu(state));

        // Icon Style submenu
        items.Add(BuildIconStyleSubmenu(state));

        items.Add(new MenuItemModel { Type = MenuItemType.Separator });

        // Sessions submenu
        var sessionChildren = new List<MenuItemModel>();
        foreach (var s in state.Sessions)
        {
            sessionChildren.Add(new MenuItemModel
            {
                Label = $"{s.Project} [{s.Status}]",
                Enabled = false,
            });
        }

        if (sessionChildren.Count == 0)
        {
            sessionChildren.Add(new MenuItemModel { Label = "(no active sessions)", Enabled = false });
        }

        items.Add(new MenuItemModel
        {
            Label = $"Sessions ({state.Sessions.Count})",
            Type = MenuItemType.Submenu,
            Children = sessionChildren,
        });

        // Workspaces submenu
        var workspaceChildren = new List<MenuItemModel>();
        foreach (var ws in state.Workspaces)
        {
            workspaceChildren.Add(new MenuItemModel
            {
                Label = ws.WorkspaceName,
                Enabled = false,
            });
        }

        if (workspaceChildren.Count == 0)
        {
            workspaceChildren.Add(new MenuItemModel { Label = "(no workspaces)", Enabled = false });
        }

        items.Add(new MenuItemModel
        {
            Label = "Workspaces",
            Type = MenuItemType.Submenu,
            Children = workspaceChildren,
        });

        items.Add(new MenuItemModel { Type = MenuItemType.Separator });

        // Folder / log items
        items.Add(new MenuItemModel { Label = "Open Config Folder", Tag = "open-config" });
        items.Add(new MenuItemModel { Label = "Open Sounds Folder", Tag = "open-sounds" });
        items.Add(new MenuItemModel { Label = "View Log", Tag = "open-log" });

        items.Add(new MenuItemModel { Type = MenuItemType.Separator });

        // Exit
        items.Add(new MenuItemModel { Label = "Exit", Tag = "exit" });

        return items;
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
        var currentStyle = state.Config.Tray.IconStyle ?? "dots";
        var isDots = !currentStyle.StartsWith("pack:", StringComparison.OrdinalIgnoreCase);

        // Dots (built-in) — always present
        children.Add(new MenuItemModel
        {
            Label = "Dots (built-in)",
            Tag = "switch-icon-style:dots",
            Checked = isDots,
        });

        // Installed graphics packs
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
