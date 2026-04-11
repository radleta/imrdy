using FluentAssertions;
using Imrdy.Core.Menus;
using Imrdy.Core.Tests.Helpers;

namespace Imrdy.Core.Tests.Menus;

public class WorkspaceMenuModelTests
{
    [Fact]
    public void Build_Workspace_HeaderShowsNameAndWorkspaceLabel()
    {
        var state = MenuTestHelper.PinnedWorkspaceState("Dev", @"C:\dev");

        var items = WorkspaceMenuModel.Build(state);

        items[0].Label.Should().Be("Dev [workspace]");
        items[0].Enabled.Should().BeFalse();
    }

    [Fact]
    public void Build_Workspace_HasSeparatorAndManageSubmenu()
    {
        var state = MenuTestHelper.PinnedWorkspaceState("Dev", @"C:\dev");

        var items = WorkspaceMenuModel.Build(state);

        items.Should().HaveCount(5);
        items[1].Type.Should().Be(MenuItemType.Separator);
        items[3].Type.Should().Be(MenuItemType.Separator);
        items[4].Label.Should().Be("Manage");
        items[4].Type.Should().Be(MenuItemType.Submenu);
    }

    [Fact]
    public void Build_Workspace_HasIconStyleSubmenu()
    {
        var state = MenuTestHelper.PinnedWorkspaceState("Dev", @"C:\dev");

        var items = WorkspaceMenuModel.Build(state);

        var iconStyle = items.First(i => i.Label == "Icon Style");
        iconStyle.Type.Should().Be(MenuItemType.Submenu);
        iconStyle.Children.Should().NotBeEmpty();
    }

    [Fact]
    public void Build_Workspace_IconStyleSubmenu_DefaultCheckedWhenNull()
    {
        var state = MenuTestHelper.PinnedWorkspaceState("Dev", @"C:\dev", iconStyle: null);

        var items = WorkspaceMenuModel.Build(state);

        var iconStyle = items.First(i => i.Label == "Icon Style");
        var defaultItem = iconStyle.Children.First(c => c.Label == "(Default)");
        defaultItem.Checked.Should().BeTrue();
        defaultItem.Tag.Should().Be("set-icon-style:(default)");
    }

    [Fact]
    public void Build_Workspace_IconStyleSubmenu_SpecificStyleChecked()
    {
        var state = MenuTestHelper.PinnedWorkspaceState("Dev", @"C:\dev", iconStyle: "triangles");

        var items = WorkspaceMenuModel.Build(state);

        var iconStyle = items.First(i => i.Label == "Icon Style");
        var defaultItem = iconStyle.Children.First(c => c.Label == "(Default)");
        var trianglesItem = iconStyle.Children.First(c => c.Label == "Triangles");
        defaultItem.Checked.Should().BeFalse();
        trianglesItem.Checked.Should().BeTrue();
    }

    [Fact]
    public void Build_Workspace_IconStyleSubmenu_Has6BuiltInStyles()
    {
        var state = MenuTestHelper.PinnedWorkspaceState("Dev", @"C:\dev");

        var items = WorkspaceMenuModel.Build(state);

        var iconStyle = items.First(i => i.Label == "Icon Style");
        var builtIns = iconStyle.Children
            .Where(c => c.Type != MenuItemType.Separator && c.Label != "(Default)")
            .ToList();
        builtIns.Should().HaveCount(6);
    }

    [Fact]
    public void Build_Workspace_IconStyleSubmenu_ShowsGraphicsPacks()
    {
        var state = MenuTestHelper.PinnedWorkspaceState(
            "Dev", @"C:\dev",
            installedGraphicsPacks: ["ghosts", "retro"]);

        var items = WorkspaceMenuModel.Build(state);

        var iconStyle = items.First(i => i.Label == "Icon Style");
        iconStyle.Children.Should().Contain(c => c.Label == "ghosts" && c.Tag == "set-icon-style:pack:ghosts");
        iconStyle.Children.Should().Contain(c => c.Label == "retro" && c.Tag == "set-icon-style:pack:retro");
    }

    [Fact]
    public void Build_Workspace_UnpinTagContainsPath()
    {
        var state = MenuTestHelper.PinnedWorkspaceState("Dev", @"C:\dev\project");

        var items = WorkspaceMenuModel.Build(state);

        var manage = items.First(i => i.Label == "Manage");
        manage.Children.Should().ContainSingle()
            .Which.Tag.Should().Be(@"unpin:C:\dev\project");
    }
}
