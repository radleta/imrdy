namespace Imrdy.Core.Menus;

public enum MenuItemType { Item, Separator, Submenu }

public sealed record MenuItemModel
{
    public string Label { get; init; } = "";
    public MenuItemType Type { get; init; } = MenuItemType.Item;
    public bool Checked { get; init; }
    public bool Enabled { get; init; } = true;
    public string? Tag { get; init; }
    public IReadOnlyList<MenuItemModel> Children { get; init; } = [];
}
