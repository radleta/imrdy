using System.Globalization;
using Imrdy.Core.Icons;

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

        items.Add(BuildIconStyleSubmenu(state));

        items.Add(new MenuItemModel { Type = MenuItemType.Separator });

        items.Add(new MenuItemModel
        {
            Label = "Manage",
            Type = MenuItemType.Submenu,
            Children = [new MenuItemModel { Label = "Unpin", Tag = $"unpin:{state.WorkspacePath}" }],
        });

        return items;
    }

    private static MenuItemModel BuildIconStyleSubmenu(WorkspaceMenuState state)
    {
        var children = new List<MenuItemModel>();
        var textInfo = CultureInfo.InvariantCulture.TextInfo;

        // (Default) — use global style
        children.Add(new MenuItemModel
        {
            Label = "(Default)",
            Tag = "set-icon-style:(default)",
            Checked = state.IconStyle is null,
        });

        children.Add(new MenuItemModel { Type = MenuItemType.Separator });

        // Built-in shape styles
        foreach (var styleName in StyleNames.BuiltInStyles)
        {
            children.Add(new MenuItemModel
            {
                Label = textInfo.ToTitleCase(styleName),
                Tag = $"set-icon-style:{styleName}",
                Checked = string.Equals(state.IconStyle, styleName, StringComparison.OrdinalIgnoreCase),
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
                    Tag = $"set-icon-style:{packStyle}",
                    Checked = string.Equals(state.IconStyle, packStyle, StringComparison.OrdinalIgnoreCase),
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
}
