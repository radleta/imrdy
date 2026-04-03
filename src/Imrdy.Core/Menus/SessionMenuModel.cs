namespace Imrdy.Core.Menus;

internal static class SessionMenuModel
{
    public static IReadOnlyList<MenuItemModel> Build(SessionMenuState state)
    {
        return
        [
            new MenuItemModel { Label = $"{state.Project} [{state.Status}]", Enabled = false },
            new MenuItemModel { Type = MenuItemType.Separator },
            new MenuItemModel
            {
                Label = "Manage",
                Type = MenuItemType.Submenu,
                Children = [new MenuItemModel { Label = "Dismiss", Tag = "dismiss" }],
            },
        ];
    }
}
