namespace Imrdy.Core.Menus;

internal static class ControllerMenuModel
{
    public static IReadOnlyList<MenuItemModel> Build(ControllerMenuState state)
    {
        var items = new List<MenuItemModel>();

        // Sound toggle
        items.Add(new MenuItemModel
        {
            Label = "Sounds",
            Checked = state.Config.SoundEnabled,
            Tag = "toggle-sound",
        });

        // Sound Pack submenu
        var packChildren = new List<MenuItemModel>();
        foreach (var pack in state.InstalledPacks)
        {
            packChildren.Add(new MenuItemModel
            {
                Label = pack,
                Checked = string.Equals(pack, state.Config.Default, StringComparison.OrdinalIgnoreCase),
                Tag = $"switch-pack:{pack}",
            });
        }

        if (packChildren.Count == 0)
        {
            packChildren.Add(new MenuItemModel { Label = "(none installed)", Enabled = false });
        }

        items.Add(new MenuItemModel
        {
            Label = "Sound Pack",
            Type = MenuItemType.Submenu,
            Children = packChildren,
        });

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
}
