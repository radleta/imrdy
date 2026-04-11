using FluentAssertions;
using Imrdy.Core.Icons;
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
    public void Build_Header_ShowsImrdy()
    {
        var state = MenuTestHelper.EmptyControllerState();

        var items = ControllerMenuModel.Build(state);

        items[0].Label.Should().Be("imrdy");
        items[0].Enabled.Should().BeFalse();
        items[1].Type.Should().Be(MenuItemType.Separator);
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

    [Fact]
    public void Build_IconStyleSubmenu_CirclesCheckedByDefault()
    {
        // Default TrayConfig.IconStyle is "dots" which normalizes to "circles"
        var state = MenuTestHelper.EmptyControllerState();

        var items = ControllerMenuModel.Build(state);

        var iconStyleMenu = items.First(i => i.Label == "Icon Style");
        iconStyleMenu.Children.First(c => c.Tag == "switch-icon-style:circles").Checked.Should().BeTrue();
        iconStyleMenu.Children.Where(c => c.Tag != "switch-icon-style:circles" && c.Type == MenuItemType.Item)
            .Should().OnlyContain(c => !c.Checked);
    }

    [Fact]
    public void Build_IconStyleSubmenu_DotsNormalizesToCircles()
    {
        var state = MenuTestHelper.EmptyControllerState() with
        {
            Config = new ImrdyConfig { Tray = new TrayConfig { IconStyle = "dots" } },
        };

        var items = ControllerMenuModel.Build(state);

        var iconStyleMenu = items.First(i => i.Label == "Icon Style");
        iconStyleMenu.Children.First(c => c.Tag == "switch-icon-style:circles").Checked.Should().BeTrue();
    }

    [Fact]
    public void Build_IconStyleSubmenu_AllSixBuiltInsPresent()
    {
        var state = MenuTestHelper.EmptyControllerState();

        var items = ControllerMenuModel.Build(state);

        var iconStyleMenu = items.First(i => i.Label == "Icon Style");
        foreach (var styleName in StyleNames.BuiltInStyles)
        {
            iconStyleMenu.Children.Should().Contain(c => c.Tag == $"switch-icon-style:{styleName}",
                $"built-in style '{styleName}' should be present");
        }
    }

    [Fact]
    public void Build_IconStyleSubmenu_BuiltInLabelsAreTitleCase()
    {
        var state = MenuTestHelper.EmptyControllerState();

        var items = ControllerMenuModel.Build(state);

        var iconStyleMenu = items.First(i => i.Label == "Icon Style");
        iconStyleMenu.Children.First(c => c.Tag == "switch-icon-style:circles").Label.Should().Be("Circles");
        iconStyleMenu.Children.First(c => c.Tag == "switch-icon-style:squares").Label.Should().Be("Squares");
        iconStyleMenu.Children.First(c => c.Tag == "switch-icon-style:triangles").Label.Should().Be("Triangles");
        iconStyleMenu.Children.First(c => c.Tag == "switch-icon-style:diamonds").Label.Should().Be("Diamonds");
        iconStyleMenu.Children.First(c => c.Tag == "switch-icon-style:hexagons").Label.Should().Be("Hexagons");
        iconStyleMenu.Children.First(c => c.Tag == "switch-icon-style:plus").Label.Should().Be("Plus");
    }

    [Fact]
    public void Build_IconStyleSubmenu_SpecificStyleChecked()
    {
        var state = MenuTestHelper.EmptyControllerState() with
        {
            Config = new ImrdyConfig { Tray = new TrayConfig { IconStyle = "triangles" } },
        };

        var items = ControllerMenuModel.Build(state);

        var iconStyleMenu = items.First(i => i.Label == "Icon Style");
        iconStyleMenu.Children.First(c => c.Tag == "switch-icon-style:triangles").Checked.Should().BeTrue();
        iconStyleMenu.Children.Where(c => c.Tag != "switch-icon-style:triangles" && c.Type == MenuItemType.Item)
            .Should().OnlyContain(c => !c.Checked);
    }

    [Fact]
    public void Build_IconStyleSubmenu_PackCheckedWhenActive()
    {
        var state = MenuTestHelper.EmptyControllerState() with
        {
            InstalledGraphicsPacks = ["my-pack"],
            Config = new ImrdyConfig { Tray = new TrayConfig { IconStyle = "pack:my-pack" } },
        };

        var items = ControllerMenuModel.Build(state);

        var iconStyleMenu = items.First(i => i.Label == "Icon Style");
        iconStyleMenu.Children.First(c => c.Label == "my-pack").Checked.Should().BeTrue();
        iconStyleMenu.Children.First(c => c.Tag == "switch-icon-style:circles").Checked.Should().BeFalse();
    }

    [Fact]
    public void Build_IconStyleSubmenu_ListsAllInstalledPacks()
    {
        var state = MenuTestHelper.EmptyControllerState() with
        {
            InstalledGraphicsPacks = ["pack-a", "pack-b"],
        };

        var items = ControllerMenuModel.Build(state);

        var iconStyleMenu = items.First(i => i.Label == "Icon Style");
        // 6 built-ins + separator + 2 packs = 9
        iconStyleMenu.Children.Should().HaveCount(9);
        iconStyleMenu.Children.Select(c => c.Label).Should().Contain("pack-a", "pack-b");
    }

    [Fact]
    public void Build_IconStyleSubmenu_EmptyPacksList_ShowsOnlySixBuiltIns()
    {
        var state = MenuTestHelper.EmptyControllerState() with
        {
            InstalledGraphicsPacks = [],
        };

        var items = ControllerMenuModel.Build(state);

        var iconStyleMenu = items.First(i => i.Label == "Icon Style");
        iconStyleMenu.Children.Should().HaveCount(6);
        iconStyleMenu.Children.Should().OnlyContain(c => c.Type == MenuItemType.Item);
    }

    [Fact]
    public void Build_IconStyleSubmenu_HasSeparatorBeforePacks()
    {
        var state = MenuTestHelper.EmptyControllerState() with
        {
            InstalledGraphicsPacks = ["my-pack"],
        };

        var items = ControllerMenuModel.Build(state);

        var iconStyleMenu = items.First(i => i.Label == "Icon Style");
        // 6 built-ins + 1 separator + 1 pack = 8
        iconStyleMenu.Children.Should().HaveCount(8);
        iconStyleMenu.Children[6].Type.Should().Be(MenuItemType.Separator);
        iconStyleMenu.Children[7].Label.Should().Be("my-pack");
    }

    [Fact]
    public void Build_OverlaySubmenu_ShowsEnabled_WhenOverlayOn()
    {
        var state = MenuTestHelper.EmptyControllerState() with
        {
            Config = new ImrdyConfig { Overlay = new OverlayConfig { Enabled = true } },
        };

        var items = ControllerMenuModel.Build(state);

        var overlayMenu = items.First(i => i.Label == "Overlay");
        overlayMenu.Children.First(c => c.Tag == "toggle-overlay").Checked.Should().BeTrue();
    }

    [Fact]
    public void Build_OverlaySubmenu_ShowsDisabled_WhenOverlayOff()
    {
        var state = MenuTestHelper.EmptyControllerState() with
        {
            Config = new ImrdyConfig { Overlay = new OverlayConfig { Enabled = false } },
        };

        var items = ControllerMenuModel.Build(state);

        var overlayMenu = items.First(i => i.Label == "Overlay");
        overlayMenu.Children.First(c => c.Tag == "toggle-overlay").Checked.Should().BeFalse();
    }

    [Fact]
    public void Build_OverlaySubmenu_ShowsPositionRadios_WithCorrectSelection()
    {
        var state = MenuTestHelper.EmptyControllerState() with
        {
            Config = new ImrdyConfig { Overlay = new OverlayConfig { Position = "bottom-left" } },
        };

        var items = ControllerMenuModel.Build(state);

        var overlayMenu = items.First(i => i.Label == "Overlay");
        overlayMenu.Children.First(c => c.Tag == "set-overlay-position:bottom-left").Checked.Should().BeTrue();
        overlayMenu.Children.First(c => c.Tag == "set-overlay-position:bottom-right").Checked.Should().BeFalse();
    }

    [Fact]
    public void Build_OverlaySubmenu_ShowsSizeRadios_WithCorrectSelection()
    {
        var state = MenuTestHelper.EmptyControllerState() with
        {
            Config = new ImrdyConfig { Overlay = new OverlayConfig { Size = 96 } },
        };

        var items = ControllerMenuModel.Build(state);

        var overlayMenu = items.First(i => i.Label == "Overlay");
        overlayMenu.Children.First(c => c.Tag == "set-overlay-size:48").Checked.Should().BeFalse();
        overlayMenu.Children.First(c => c.Tag == "set-overlay-size:64").Checked.Should().BeFalse();
        overlayMenu.Children.First(c => c.Tag == "set-overlay-size:96").Checked.Should().BeTrue();
        overlayMenu.Children.First(c => c.Tag == "set-overlay-size:128").Checked.Should().BeFalse();
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
