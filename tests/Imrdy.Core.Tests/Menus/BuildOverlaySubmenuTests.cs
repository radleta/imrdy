using FluentAssertions;
using Imrdy.Core.Menus;
using Imrdy.Core.Tests.Helpers;

namespace Imrdy.Core.Tests.Menus;

public class BuildOverlaySubmenuTests
{
    // ── Position ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("top-left")]
    [InlineData("top-center")]
    [InlineData("top-right")]
    [InlineData("bottom-left")]
    [InlineData("bottom-center")]
    [InlineData("bottom-right")]
    public void BuildOverlaySubmenu_Position_ExactlyOneChecked(string position)
    {
        var state = MenuTestHelper.EmptyControllerState() with
        {
            Config = new ImrdyConfig { Overlay = new OverlayConfig { Position = position } },
        };

        var menu = ControllerMenuModel.BuildOverlaySubmenu(state);

        var positionItems = menu.Children
            .Where(c => c.Tag?.StartsWith("set-overlay-position:") == true)
            .ToList();

        positionItems.Should().HaveCount(6, "all six anchors must be present");
        positionItems.Where(c => c.Checked).Should().ContainSingle("exactly one position must be checked")
            .Which.Tag.Should().Be($"set-overlay-position:{position}");
    }

    [Fact]
    public void BuildOverlaySubmenu_Position_UnknownValueDefaultsToBottomRight()
    {
        var state = MenuTestHelper.EmptyControllerState() with
        {
            Config = new ImrdyConfig { Overlay = new OverlayConfig { Position = "garbage-input" } },
        };

        var menu = ControllerMenuModel.BuildOverlaySubmenu(state);

        var positionItems = menu.Children
            .Where(c => c.Tag?.StartsWith("set-overlay-position:") == true)
            .ToList();

        positionItems.Where(c => c.Checked).Should().ContainSingle()
            .Which.Tag.Should().Be("set-overlay-position:bottom-right");
    }

    // ── Spacing ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(16)]
    public void BuildOverlaySubmenu_Spacing_CorrectItemChecked(int spacing)
    {
        var state = MenuTestHelper.EmptyControllerState() with
        {
            Config = new ImrdyConfig { Overlay = new OverlayConfig { Spacing = spacing } },
        };

        var menu = ControllerMenuModel.BuildOverlaySubmenu(state);

        var spacingItems = menu.Children
            .Where(c => c.Tag?.StartsWith("set-overlay-spacing:") == true)
            .ToList();

        spacingItems.Should().HaveCount(4, "four spacing presets must be present");
        spacingItems.Where(c => c.Checked).Should().ContainSingle("exactly one spacing item must be checked")
            .Which.Tag.Should().Be($"set-overlay-spacing:{spacing}");
    }

    // ── Monitors ─────────────────────────────────────────────────────────────

    [Fact]
    public void BuildOverlaySubmenu_Monitors_CountEqualsMonitorsList()
    {
        var monitors = new[] { "Monitor 1 (1920×1080)", "Monitor 2 (2560×1440)" };
        var state = MenuTestHelper.EmptyControllerState() with
        {
            Monitors = monitors,
            Config = new ImrdyConfig { Overlay = new OverlayConfig { Monitor = 1 } },
        };

        var menu = ControllerMenuModel.BuildOverlaySubmenu(state);

        var monitorItems = menu.Children
            .Where(c => c.Tag?.StartsWith("set-overlay-monitor:") == true)
            .ToList();

        monitorItems.Should().HaveCount(monitors.Length, "one item per monitor label");
        monitorItems.Where(c => c.Checked).Should().ContainSingle("exactly one monitor must be checked")
            .Which.Tag.Should().Be("set-overlay-monitor:1");
    }

    [Fact]
    public void BuildOverlaySubmenu_Monitors_EmptyList_ProducesNoMonitorItems()
    {
        var state = MenuTestHelper.EmptyControllerState() with
        {
            Monitors = [],
        };

        var menu = ControllerMenuModel.BuildOverlaySubmenu(state);

        menu.Children.Where(c => c.Tag != null && c.Tag.StartsWith("set-overlay-monitor:"))
            .Should().BeEmpty("no monitor items when Monitors list is empty");
    }

    // ── Lock ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BuildOverlaySubmenu_Lock_CheckedReflectsOverlayLocked(bool locked)
    {
        var state = MenuTestHelper.EmptyControllerState() with
        {
            Config = new ImrdyConfig { Overlay = new OverlayConfig { Locked = locked } },
        };

        var menu = ControllerMenuModel.BuildOverlaySubmenu(state);

        var lockItem = menu.Children.First(c => c.Tag == "toggle-overlay-lock");
        lockItem.Checked.Should().Be(locked);
    }
}
