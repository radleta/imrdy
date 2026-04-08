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
    public void Build_EmptyState_SoundPackSubmenuHasRandomAndNone()
    {
        var state = MenuTestHelper.EmptyControllerState();

        var items = ControllerMenuModel.Build(state);

        var packMenu = items.First(i => i.Label == "Sound Pack");
        // Random + None (no installed packs, no separator)
        packMenu.Children.Should().HaveCount(2);
        packMenu.Children.First().Label.Should().Be("Random");
        packMenu.Children.Last().Label.Should().Be("(None)");
    }

    [Fact]
    public void Build_EmptyState_EnabledPacksSubmenuShowsNoneInstalled()
    {
        var state = MenuTestHelper.EmptyControllerState();

        var items = ControllerMenuModel.Build(state);

        var enabledMenu = items.First(i => i.Label == "Enabled Packs");
        enabledMenu.Children.Should().ContainSingle()
            .Which.Label.Should().Be("(none installed)");
        enabledMenu.Children.Single().Enabled.Should().BeFalse();
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
    public void Build_ActiveState_SoundPackSubmenu_RandomIsChecked()
    {
        var state = MenuTestHelper.ActiveControllerState();

        var items = ControllerMenuModel.Build(state);

        var packMenu = items.First(i => i.Label == "Sound Pack");
        // Random + assistant + retro + separator + (None) = 5
        packMenu.Children.Should().HaveCount(5);
        packMenu.Children.First(c => c.Tag == "switch-pack:random").Checked.Should().BeTrue();
        packMenu.Children.First(c => c.Tag == "switch-pack:assistant").Checked.Should().BeFalse();
        packMenu.Children.First(c => c.Tag == "switch-pack:retro").Checked.Should().BeFalse();
        packMenu.Children.First(c => c.Tag == "switch-pack:").Checked.Should().BeFalse();
    }

    [Fact]
    public void Build_SpecificDefault_PackIsChecked()
    {
        var state = MenuTestHelper.ActiveControllerState() with
        {
            Config = new ImrdyConfig { Sound = new SoundConfig { DefaultPack = "retro" } }
        };

        var items = ControllerMenuModel.Build(state);

        var packMenu = items.First(i => i.Label == "Sound Pack");
        packMenu.Children.First(c => c.Tag == "switch-pack:random").Checked.Should().BeFalse();
        packMenu.Children.First(c => c.Tag == "switch-pack:retro").Checked.Should().BeTrue();
        packMenu.Children.First(c => c.Tag == "switch-pack:assistant").Checked.Should().BeFalse();
    }

    [Fact]
    public void Build_NoneDefault_NoneIsChecked()
    {
        var state = MenuTestHelper.ActiveControllerState() with
        {
            Config = new ImrdyConfig { Sound = new SoundConfig { DefaultPack = "" } }
        };

        var items = ControllerMenuModel.Build(state);

        var packMenu = items.First(i => i.Label == "Sound Pack");
        packMenu.Children.First(c => c.Tag == "switch-pack:random").Checked.Should().BeFalse();
        packMenu.Children.First(c => c.Tag == "switch-pack:").Checked.Should().BeTrue();
    }

    [Fact]
    public void Build_ActiveState_EnabledPacksSubmenu_AllEnabled()
    {
        var state = MenuTestHelper.ActiveControllerState();

        var items = ControllerMenuModel.Build(state);

        var enabledMenu = items.First(i => i.Label == "Enabled Packs");
        enabledMenu.Children.Should().HaveCount(2);
        enabledMenu.Children.Should().OnlyContain(c => c.Checked == true);
        enabledMenu.Children.First(c => c.Tag == "toggle-pack-enabled:assistant").Should().NotBeNull();
        enabledMenu.Children.First(c => c.Tag == "toggle-pack-enabled:retro").Should().NotBeNull();
    }

    [Fact]
    public void Build_DisabledPack_EnabledPacksSubmenu_ShowsUnchecked()
    {
        var state = MenuTestHelper.ActiveControllerState() with
        {
            Config = new ImrdyConfig
            {
                Sound = new SoundConfig
                {
                    DefaultPack = "random",
                    DisabledPacks = ["retro"]
                }
            }
        };

        var items = ControllerMenuModel.Build(state);

        var enabledMenu = items.First(i => i.Label == "Enabled Packs");
        enabledMenu.Children.First(c => c.Label == "assistant").Checked.Should().BeTrue();
        enabledMenu.Children.First(c => c.Label == "retro").Checked.Should().BeFalse();
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
        allTags.Should().Contain("switch-pack:random");
        allTags.Should().Contain("switch-pack:assistant");
        allTags.Should().Contain("toggle-pack-enabled:assistant");
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

        separatorIndices.Should().HaveCount(4);
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
