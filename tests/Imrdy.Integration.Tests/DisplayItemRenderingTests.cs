using FluentAssertions;
using Imrdy.Core.Display;
using Xunit;

namespace Imrdy.Integration.Tests;

[Trait("Category", "Integration")]
public class DisplayItemRenderingTests
{
    private static DisplayItemInput MakeSession(int? desktopIndex, string id)
        => new(id, DisplayItemType.Session, "idle", desktopIndex, "circles", 0, true, $"Session-{id}");

    private static DisplayItemInput MakeWorkspace(int? desktopIndex, string id)
        => new(id, DisplayItemType.Workspace, "idle", desktopIndex, "circles", 0, true, $"Workspace-{id}");

    [Fact]
    public void OverlayItemOrdering_DesktopIndexThenType()
    {
        // Sessions on desktops [3, 1, 1] and a workspace on desktop 2
        var inputs = new DisplayItemInput[]
        {
            MakeSession(3, "s-d3"),
            MakeSession(1, "s-d1a"),
            MakeSession(1, "s-d1b"),
            MakeWorkspace(2, "w-d2"),
        };

        var result = DisplayItemCollection.Build(inputs, trayEnabled: true);

        // Expected ForOverlay order: D1-session, D1-session, D2-workspace, D3-session
        result.ForOverlay.Should().HaveCount(4);
        result.ForOverlay[0].Should().Match<DisplayItem>(x => x.DesktopIndex == 1 && x.ItemType == DisplayItemType.Session);
        result.ForOverlay[1].Should().Match<DisplayItem>(x => x.DesktopIndex == 1 && x.ItemType == DisplayItemType.Session);
        result.ForOverlay[2].Should().Match<DisplayItem>(x => x.DesktopIndex == 2 && x.ItemType == DisplayItemType.Workspace);
        result.ForOverlay[3].Should().Match<DisplayItem>(x => x.DesktopIndex == 3 && x.ItemType == DisplayItemType.Session);
    }
}
