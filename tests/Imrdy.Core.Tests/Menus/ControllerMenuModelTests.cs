using FluentAssertions;
using Imrdy.Core.Menus;
using Imrdy.Core.Tests.Helpers;

namespace Imrdy.Core.Tests.Menus;

public class ControllerMenuModelTests
{
    [Fact]
    public void Build_EmptyState_ContainsExitItem()
    {
        var state = MenuTestHelper.EmptyControllerState();

        var items = ControllerMenuModel.Build(state);

        items.Should().Contain(i => i.Tag == "exit");
        items.Last().Tag.Should().Be("exit");
    }

    [Fact]
    public void Build_EmptyState_SessionsSubmenuShowsNoActiveSessions()
    {
        var state = MenuTestHelper.EmptyControllerState();

        var items = ControllerMenuModel.Build(state);

        var sessionsMenu = items.First(i => i.Label.StartsWith("Sessions"));
        sessionsMenu.Children.Should().ContainSingle()
            .Which.Label.Should().Be("(no active sessions)");
    }

    [Fact]
    public void Build_EmptyState_PacksSubmenuShowsNoneInstalled()
    {
        var state = MenuTestHelper.EmptyControllerState();

        var items = ControllerMenuModel.Build(state);

        var packMenu = items.First(i => i.Label == "Sound Pack");
        packMenu.Children.Should().ContainSingle()
            .Which.Label.Should().Be("(none installed)");
        packMenu.Children.Single().Enabled.Should().BeFalse();
    }

    [Fact]
    public void Build_ActiveState_SoundToggleIsChecked()
    {
        var state = MenuTestHelper.ActiveControllerState();

        var items = ControllerMenuModel.Build(state);

        var toggle = items.First(i => i.Tag == "toggle-sound");
        toggle.Checked.Should().BeTrue();
        toggle.Label.Should().Be("Sounds");
    }

    [Fact]
    public void Build_SoundDisabled_SoundToggleIsUnchecked()
    {
        var state = MenuTestHelper.SoundDisabledControllerState();

        var items = ControllerMenuModel.Build(state);

        var toggle = items.First(i => i.Tag == "toggle-sound");
        toggle.Checked.Should().BeFalse();
    }

    [Fact]
    public void Build_ActiveState_PackItemsWithCorrectCheckedState()
    {
        var state = MenuTestHelper.ActiveControllerState();

        var items = ControllerMenuModel.Build(state);

        var packMenu = items.First(i => i.Label == "Sound Pack");
        packMenu.Children.Should().HaveCount(2);
        packMenu.Children.First(c => c.Tag == "switch-pack:assistant").Checked.Should().BeTrue();
        packMenu.Children.First(c => c.Tag == "switch-pack:retro").Checked.Should().BeFalse();
    }

    [Fact]
    public void Build_ActiveState_SessionsSubmenuHasThreeChildren()
    {
        var state = MenuTestHelper.ActiveControllerState();

        var items = ControllerMenuModel.Build(state);

        var sessionsMenu = items.First(i => i.Label == "Sessions (3)");
        sessionsMenu.Children.Should().HaveCount(3);
        sessionsMenu.Children.Should().OnlyContain(c => c.Enabled == false);
    }

    [Fact]
    public void Build_ActiveState_WorkspacesSubmenuHasChildren()
    {
        var state = MenuTestHelper.ActiveControllerState();

        var items = ControllerMenuModel.Build(state);

        var wsMenu = items.First(i => i.Label == "Workspaces");
        wsMenu.Children.Should().ContainSingle()
            .Which.Label.Should().Be("Dev");
    }

    [Fact]
    public void Build_ActiveState_AllTagsPresent()
    {
        var state = MenuTestHelper.ActiveControllerState();

        var items = ControllerMenuModel.Build(state);
        var allTags = FlattenTags(items);

        allTags.Should().Contain("toggle-sound");
        allTags.Should().Contain("switch-pack:assistant");
        allTags.Should().Contain("open-config");
        allTags.Should().Contain("open-sounds");
        allTags.Should().Contain("open-log");
        allTags.Should().Contain("exit");
    }

    [Fact]
    public void Build_ActiveState_ThreeSeparatorsInCorrectPositions()
    {
        var state = MenuTestHelper.ActiveControllerState();

        var items = ControllerMenuModel.Build(state);

        var separatorIndices = items
            .Select((item, index) => (item, index))
            .Where(x => x.item.Type == MenuItemType.Separator)
            .Select(x => x.index)
            .ToList();

        separatorIndices.Should().HaveCount(3);
    }

    private static List<string> FlattenTags(IReadOnlyList<MenuItemModel> items)
    {
        var tags = new List<string>();
        foreach (var item in items)
        {
            if (item.Tag is not null)
            {
                tags.Add(item.Tag);
            }

            tags.AddRange(FlattenTags(item.Children));
        }

        return tags;
    }
}
