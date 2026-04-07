namespace Imrdy.Core.Menus;

internal static class WorkspaceMenuModel
{
    public static IReadOnlyList<MenuItemModel> Build(WorkspaceMenuState state)
    {
        var items = new List<MenuItemModel>
        {
            new() { Label = $"{state.WorkspaceName} [workspace]", Enabled = false },
            new() { Type = MenuItemType.Separator },
        };

        if (state.DesktopAvailable)
        {
            items.Add(new MenuItemModel
            {
                Label = "Assign to This Desktop",
                Tag = "assign-desktop",
            });

            if (state.DesktopCount.HasValue)
            {
                var desktopChildren = new List<MenuItemModel>();
                for (var i = 0; i < state.DesktopCount.Value; i++)
                {
                    desktopChildren.Add(new MenuItemModel
                    {
                        Label = $"Desktop {i + 1}",
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

            items.Add(new MenuItemModel { Type = MenuItemType.Separator });
        }

        items.Add(new MenuItemModel
        {
            Label = "Manage",
            Type = MenuItemType.Submenu,
            Children = [new MenuItemModel { Label = "Unpin", Tag = $"unpin:{state.WorkspacePath}" }],
        });

        return items;
    }
}
