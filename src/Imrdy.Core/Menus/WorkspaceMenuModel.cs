namespace Imrdy.Core.Menus;

internal static class WorkspaceMenuModel
{
    public static IReadOnlyList<MenuItemModel> Build(WorkspaceMenuState state)
    {
        return
        [
            new MenuItemModel { Label = $"{state.WorkspaceName} [workspace]", Enabled = false },
            new MenuItemModel { Type = MenuItemType.Separator },
            new MenuItemModel
            {
                Label = "Manage",
                Type = MenuItemType.Submenu,
                Children = [new MenuItemModel { Label = "Unpin", Tag = $"unpin:{state.WorkspacePath}" }],
            },
        ];
    }
}
