using FluentAssertions;
using Imrdy.Core.Menus;
using Imrdy.Core.Tests.Helpers;

namespace Imrdy.Core.Tests.Menus;

public class SessionMenuModelTests
{
    [Fact]
    public void Build_FullMenu_HasAllTopLevelItems()
    {
        var state = MenuTestHelper.SingleSessionState("my-project", "idle");

        var items = SessionMenuModel.Build(state);

        // Header, separator, Switch, Assign, SetDesktop, SoundPack, separator, PinAsWorkspace, separator, Manage, separator, Exit
        items.Should().HaveCount(12);
    }

    [Fact]
    public void Build_SwitchDesktopTag_Present()
    {
        var state = MenuTestHelper.SingleSessionState("proj", "idle");

        var items = SessionMenuModel.Build(state);

        items.Should().Contain(i => i.Tag == "switch-desktop");
    }

    [Fact]
    public void Build_AssignDesktopTag_Present()
    {
        var state = MenuTestHelper.SingleSessionState("proj", "idle");

        var items = SessionMenuModel.Build(state);

        items.Should().Contain(i => i.Tag == "assign-desktop");
    }

    [Fact]
    public void Build_SetDesktopSubmenu_ListsDesktops()
    {
        var state = MenuTestHelper.SingleSessionState("proj", "idle");

        var items = SessionMenuModel.Build(state);

        var setDesktop = items.First(i => i.Label == "Set Desktop");
        setDesktop.Type.Should().Be(MenuItemType.Submenu);
        setDesktop.Children.Should().HaveCount(4);
        setDesktop.Children[0].Tag.Should().Be("set-desktop:auto");
        setDesktop.Children[0].Checked.Should().BeFalse();
        setDesktop.Children[1].Tag.Should().Be("set-desktop:0");
        setDesktop.Children[1].Checked.Should().BeTrue();
        setDesktop.Children[2].Tag.Should().Be("set-desktop:1");
        setDesktop.Children[2].Checked.Should().BeFalse();
        setDesktop.Children[3].Tag.Should().Be("set-desktop:2");
        setDesktop.Children[3].Checked.Should().BeFalse();
    }

    [Fact]
    public void Build_SetDesktopSubmenu_HiddenWhenUnavailable()
    {
        var state = MenuTestHelper.SessionNoDesktop();

        var items = SessionMenuModel.Build(state);

        items.Should().NotContain(i => i.Label == "Set Desktop");
    }

    [Fact]
    public void Build_SoundPackSubmenu_ListsPacks()
    {
        var state = MenuTestHelper.SingleSessionState("proj", "idle");

        var items = SessionMenuModel.Build(state);

        var soundPack = items.First(i => i.Label == "Sound Pack");
        soundPack.Type.Should().Be(MenuItemType.Submenu);
        // (None) + assistant + retro = 3
        soundPack.Children.Should().HaveCount(3);
        soundPack.Children.Should().Contain(c => c.Tag == "set-pack:assistant" && c.Checked);
        soundPack.Children.Should().Contain(c => c.Tag == "set-pack:retro" && !c.Checked);
    }

    [Fact]
    public void Build_SoundPackSubmenu_NoneInstalledShowsDisabled()
    {
        var state = MenuTestHelper.SessionNoPacks();

        var items = SessionMenuModel.Build(state);

        var soundPack = items.First(i => i.Label == "Sound Pack");
        soundPack.Children.Should().ContainSingle()
            .Which.Should().Match<MenuItemModel>(c =>
                c.Label == "(none installed)" && !c.Enabled);
    }

    [Fact]
    public void Build_SoundPackSubmenu_NoneOptionPresent()
    {
        var state = MenuTestHelper.SingleSessionState("proj", "idle");

        var items = SessionMenuModel.Build(state);

        var soundPack = items.First(i => i.Label == "Sound Pack");
        soundPack.Children[0].Label.Should().Be("(None)");
        soundPack.Children[0].Tag.Should().Be("set-pack:(none)");
    }

    [Fact]
    public void Build_ManageSubmenu_HasClearDumpClearAll()
    {
        var state = MenuTestHelper.SingleSessionState("proj", "idle");

        var items = SessionMenuModel.Build(state);

        var manage = items.First(i => i.Label == "Manage");
        manage.Type.Should().Be(MenuItemType.Submenu);
        manage.Children.Should().HaveCount(3);
        manage.Children[0].Tag.Should().Be("clear");
        manage.Children[1].Tag.Should().Be("dump-state");
        manage.Children[2].Tag.Should().Be("clear-all");
    }

    [Fact]
    public void Build_ExitTag_Present()
    {
        var state = MenuTestHelper.SingleSessionState("proj", "idle");

        var items = SessionMenuModel.Build(state);

        items.Should().Contain(i => i.Tag == "exit" && i.Label == "Exit Monitor");
    }

    [Fact]
    public void Build_Header_ShowsProjectAndStatus()
    {
        var state = MenuTestHelper.SingleSessionState("my-project", "idle");

        var items = SessionMenuModel.Build(state);

        items[0].Label.Should().Be("my-project [idle]");
        items[0].Enabled.Should().BeFalse();
        items[1].Type.Should().Be(MenuItemType.Separator);
    }

    [Fact]
    public void Build_Header_FallsBackToSessionId()
    {
        var state = new SessionMenuState
        {
            SessionId = "abc-123",
            Status = "busy",
            Project = null,
            DesktopAvailable = false,
        };

        var items = SessionMenuModel.Build(state);

        items[0].Label.Should().Be("abc-123 [busy]");
    }

    [Fact]
    public void Build_NotPinned_ShowsPinAsWorkspace()
    {
        var state = MenuTestHelper.SingleSessionState("proj", "idle");

        var items = SessionMenuModel.Build(state);

        items.Should().Contain(i => i.Tag == "pin-workspace" && i.Label == "Pin as Workspace");
        items.Should().NotContain(i => i.Tag == "unpin-workspace");
    }

    [Fact]
    public void Build_Pinned_ShowsUnpinWorkspace()
    {
        var state = MenuTestHelper.SingleSessionState("proj", "idle") with { IsPinned = true };

        var items = SessionMenuModel.Build(state);

        items.Should().Contain(i => i.Tag == "unpin-workspace" && i.Label == "Unpin Workspace");
        items.Should().NotContain(i => i.Tag == "pin-workspace");
    }

    [Fact]
    public void Build_SetDesktop_AutoCheckedWhenNull()
    {
        var state = MenuTestHelper.SingleSessionState("proj", "idle") with { DesktopIndex = null };

        var items = SessionMenuModel.Build(state);

        var setDesktop = items.First(i => i.Label == "Set Desktop");
        setDesktop.Children[0].Tag.Should().Be("set-desktop:auto");
        setDesktop.Children[0].Checked.Should().BeTrue();
        setDesktop.Children.Skip(1).Should().OnlyContain(c => c.Type == MenuItemType.Item && !c.Checked);
    }

    [Fact]
    public void Build_DesktopUnavailable_DisablesSwitchAndAssign()
    {
        var state = MenuTestHelper.SessionNoDesktop();

        var items = SessionMenuModel.Build(state);

        items.First(i => i.Tag == "switch-desktop").Enabled.Should().BeFalse();
        items.First(i => i.Tag == "assign-desktop").Enabled.Should().BeFalse();
    }
}
