namespace Imrdy.Core.Menus;

internal static class SessionMenuModel
{
    public static IReadOnlyList<MenuItemModel> Build(SessionMenuState state)
    {
        var items = new List<MenuItemModel>
        {
            new()
            {
                Label = "Switch to Desktop",
                Tag = "switch-desktop",
                Enabled = state.DesktopAvailable,
            },
            new()
            {
                Label = "Assign to This Desktop",
                Tag = "assign-desktop",
                Enabled = state.DesktopAvailable,
            },
        };

        if (state.DesktopAvailable && state.DesktopCount.HasValue)
        {
            var desktopChildren = new List<MenuItemModel>();
            for (var i = 0; i < state.DesktopCount.Value; i++)
            {
                desktopChildren.Add(new MenuItemModel
                {
                    Label = $"Desktop {i}",
                    Tag = $"set-desktop:{i}",
                    Checked = i == state.DesktopIndex,
                });
            }

            items.Add(new MenuItemModel
            {
                Label = "Set Desktop",
                Type = MenuItemType.Submenu,
                Children = desktopChildren,
            });
        }

        items.Add(BuildSoundPackSubmenu(state));

        items.Add(new MenuItemModel { Type = MenuItemType.Separator });

        items.Add(new MenuItemModel { Label = "Pin as Workspace", Tag = "pin-workspace" });

        items.Add(new MenuItemModel { Type = MenuItemType.Separator });

        items.Add(new MenuItemModel
        {
            Label = "Manage",
            Type = MenuItemType.Submenu,
            Children =
            [
                new MenuItemModel { Label = "Clear This Session", Tag = "clear" },
                new MenuItemModel { Label = "Dump State", Tag = "dump-state" },
                new MenuItemModel { Label = "Clear All Sessions", Tag = "clear-all" },
            ],
        });

        items.Add(new MenuItemModel { Type = MenuItemType.Separator });

        items.Add(new MenuItemModel { Label = "Exit Monitor", Tag = "exit" });

        return items;
    }

    private static MenuItemModel BuildSoundPackSubmenu(SessionMenuState state)
    {
        var children = new List<MenuItemModel>();

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
            children.Add(new MenuItemModel
            {
                Label = "(None)",
                Tag = "set-pack:(none)",
                Checked = state.SoundPack is null,
            });

            foreach (var pack in state.InstalledPacks)
            {
                children.Add(new MenuItemModel
                {
                    Label = pack,
                    Tag = $"set-pack:{pack}",
                    Checked = string.Equals(pack, state.SoundPack, StringComparison.OrdinalIgnoreCase),
                });
            }
        }

        return new MenuItemModel
        {
            Label = "Sound Pack",
            Type = MenuItemType.Submenu,
            Children = children,
        };
    }
}
