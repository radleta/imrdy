using FluentAssertions;
using Imrdy.Core.Display;

namespace Imrdy.Core.Tests.Menus;

public class DisplayItemCollectionTests
{
    private static DisplayItemInput MakeSession(int? desktopIndex, string id = "s1", bool isVisible = true)
        => new(id, DisplayItemType.Session, "idle", desktopIndex, "circles", 0, isVisible, $"Label-{id}");

    private static DisplayItemInput MakeWorkspace(int? desktopIndex, string id = "w1", bool isVisible = true)
        => new(id, DisplayItemType.Workspace, "idle", desktopIndex, "circles", 0, isVisible, $"Label-{id}");

    [Fact]
    public void Build_SortsByDesktopIndexAscending()
    {
        var inputs = new[]
        {
            MakeSession(3, "s3"),
            MakeSession(1, "s1"),
            MakeSession(2, "s2"),
        };

        var result = DisplayItemCollection.Build(inputs, trayEnabled: true);

        result.ForOverlay.Select(x => x.DesktopIndex).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Build_NullDesktopIndexLast()
    {
        var inputs = new[]
        {
            MakeSession(null, "sNull"),
            MakeSession(1, "s1"),
            MakeSession(2, "s2"),
        };

        var result = DisplayItemCollection.Build(inputs, trayEnabled: true);

        result.ForOverlay[0].DesktopIndex.Should().Be(1);
        result.ForOverlay[1].DesktopIndex.Should().Be(2);
        result.ForOverlay[2].DesktopIndex.Should().BeNull();
    }

    [Fact]
    public void Build_SessionsBeforeWorkspacesWithinSameDesktop()
    {
        var inputs = new[]
        {
            MakeWorkspace(1, "w1"),
            MakeSession(1, "s1"),
        };

        var result = DisplayItemCollection.Build(inputs, trayEnabled: true);

        result.ForOverlay[0].ItemType.Should().Be(DisplayItemType.Session);
        result.ForOverlay[1].ItemType.Should().Be(DisplayItemType.Workspace);
    }

    [Fact]
    public void Build_FiltersOutInvisible()
    {
        var inputs = new[]
        {
            MakeSession(1, "visible", isVisible: true),
            MakeSession(2, "hidden", isVisible: false),
        };

        var result = DisplayItemCollection.Build(inputs, trayEnabled: true);

        result.ForOverlay.Should().ContainSingle()
            .Which.Id.Should().Be("visible");
    }

    [Fact]
    public void Build_TrayDisabled_ForTrayEmpty_ForOverlayFull()
    {
        var inputs = new[]
        {
            MakeSession(1, "s1"),
            MakeSession(2, "s2"),
        };

        var result = DisplayItemCollection.Build(inputs, trayEnabled: false);

        result.ForTray.Count.Should().Be(0);
        result.ForOverlay.Count.Should().Be(2);
    }

    [Fact]
    public void Build_TrayEnabled_ForTrayEqualsForOverlay()
    {
        var inputs = new[]
        {
            MakeSession(1, "s1"),
            MakeSession(2, "s2"),
        };

        var result = DisplayItemCollection.Build(inputs, trayEnabled: true);

        result.ForTray.Should().Equal(result.ForOverlay);
    }

    [Fact]
    public void Build_EmptyInput_BothViewsEmpty()
    {
        var result = DisplayItemCollection.Build([], trayEnabled: true);

        result.ForTray.Should().BeEmpty();
        result.ForOverlay.Should().BeEmpty();
    }

    [Fact]
    public void DisplayItem_RecordEquality()
    {
        var a = new DisplayItem("id1", DisplayItemType.Session, "idle", 1, "circles", 0, true, "Label");
        var b = new DisplayItem("id1", DisplayItemType.Session, "idle", 1, "circles", 0, true, "Label");

        a.Should().Be(b);
    }

    [Fact]
    public void DisplayItem_DifferentItemType_NotEqual()
    {
        var a = new DisplayItem("id1", DisplayItemType.Session, "idle", 1, "circles", 0, true, "Label");
        var b = new DisplayItem("id1", DisplayItemType.Workspace, "idle", 1, "circles", 0, true, "Label");

        a.Should().NotBe(b);
    }
}
