using System.Diagnostics;
using Imrdy.Core.Menus;
using Microsoft.Extensions.Logging;

namespace Imrdy.Windows.Menus;

internal static class MenuRenderer
{
    public static void Apply(
        ContextMenuStrip menu,
        IReadOnlyList<MenuItemModel> items,
        Action<string>? onItemClick,
        ILogger? logger)
    {
        Debug.Assert(Application.MessageLoop, "MenuRenderer.Apply must be called on the UI thread");

        menu.Items.Clear();

        foreach (var model in items)
        {
            menu.Items.Add(CreateItem(model, onItemClick));
        }
    }

    private static ToolStripItem CreateItem(MenuItemModel model, Action<string>? onItemClick)
    {
        if (model.Type == MenuItemType.Separator)
        {
            return new ToolStripSeparator();
        }

        var item = new ToolStripMenuItem(model.Label)
        {
            Checked = model.Checked,
            Enabled = model.Enabled,
        };

        if (model.Tag is not null && onItemClick is not null)
        {
            var tag = model.Tag;
            item.Click += (_, _) => onItemClick(tag);
        }

        foreach (var child in model.Children)
        {
            item.DropDownItems.Add(CreateItem(child, onItemClick));
        }

        return item;
    }
}
