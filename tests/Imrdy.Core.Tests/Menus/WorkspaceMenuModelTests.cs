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

        items.Should().HaveCount(3);
        items[1].Type.Should().Be(MenuItemType.Separator);
        items[2].Label.Should().Be("Manage");
        items[2].Type.Should().Be(MenuItemType.Submenu);
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
