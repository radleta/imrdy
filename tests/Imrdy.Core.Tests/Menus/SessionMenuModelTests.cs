using FluentAssertions;
using Imrdy.Core.Menus;
using Imrdy.Core.Tests.Helpers;

namespace Imrdy.Core.Tests.Menus;

public class SessionMenuModelTests
{
    [Fact]
    public void Build_SingleSession_HeaderShowsProjectAndStatus()
    {
        var state = MenuTestHelper.SingleSessionState("my-project", "idle");

        var items = SessionMenuModel.Build(state);

        items[0].Label.Should().Be("my-project [idle]");
        items[0].Enabled.Should().BeFalse();
    }

    [Fact]
    public void Build_NullProject_HeaderHandlesGracefully()
    {
        var state = MenuTestHelper.SingleSessionState(null, "busy");

        var items = SessionMenuModel.Build(state);

        items[0].Label.Should().Be(" [busy]");
        items[0].Enabled.Should().BeFalse();
    }

    [Fact]
    public void Build_SingleSession_HasSeparatorAndManageSubmenu()
    {
        var state = MenuTestHelper.SingleSessionState("proj", "idle");

        var items = SessionMenuModel.Build(state);

        items.Should().HaveCount(3);
        items[1].Type.Should().Be(MenuItemType.Separator);
        items[2].Label.Should().Be("Manage");
        items[2].Type.Should().Be(MenuItemType.Submenu);
    }

    [Fact]
    public void Build_SingleSession_DismissTagPresent()
    {
        var state = MenuTestHelper.SingleSessionState("proj", "idle");

        var items = SessionMenuModel.Build(state);

        var manage = items.First(i => i.Label == "Manage");
        manage.Children.Should().ContainSingle()
            .Which.Tag.Should().Be("dismiss");
    }
}
