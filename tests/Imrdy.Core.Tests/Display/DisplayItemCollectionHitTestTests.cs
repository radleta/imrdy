using FluentAssertions;
using Imrdy.Core.Display;

namespace Imrdy.Core.Tests.Display;

/// <summary>
/// Decision-table tests for <see cref="DisplayItemCollection.TryGetItemAtClientPoint"/>.
/// Fixture: iconSize = 16, spacing = 4, slot width = 20.
/// </summary>
public class DisplayItemCollectionHitTestTests
{
    private const int IconSize = 16;
    private const int Spacing = 4;

    private static DisplayItem MakeItem(string id) =>
        new(id, DisplayItemType.Session, "idle", 0, "circles", 0, true, id);

    private static readonly DisplayItem A = MakeItem("A");
    private static readonly DisplayItem B = MakeItem("B");

    // Row 1: empty list, clientX = 0
    [Fact]
    public void EmptyList_ClientX0_ReturnsFalse()
    {
        var result = DisplayItemCollection.TryGetItemAtClientPoint(
            Array.Empty<DisplayItem>(), clientX: 0, IconSize, Spacing,
            out var hit, out var index);

        result.Should().BeFalse();
        hit.Should().BeNull();
        index.Should().Be(-1);
    }

    // Row 2: empty list, large clientX
    [Fact]
    public void EmptyList_LargeClientX_ReturnsFalse()
    {
        var result = DisplayItemCollection.TryGetItemAtClientPoint(
            Array.Empty<DisplayItem>(), clientX: 1000, IconSize, Spacing,
            out var hit, out var index);

        result.Should().BeFalse();
        hit.Should().BeNull();
        index.Should().Be(-1);
    }

    // Row 3: negative clientX
    [Fact]
    public void NegativeClientX_ReturnsFalse()
    {
        var items = new[] { A };
        var result = DisplayItemCollection.TryGetItemAtClientPoint(
            items, clientX: -1, IconSize, Spacing,
            out var hit, out var index);

        result.Should().BeFalse();
        hit.Should().BeNull();
        index.Should().Be(-1);
    }

    // Row 4: slot 0, leftmost hit zone (clientX = 0)
    [Fact]
    public void Slot0_LeftmostHitZone_ReturnsHit()
    {
        var items = new[] { A };
        var result = DisplayItemCollection.TryGetItemAtClientPoint(
            items, clientX: 0, IconSize, Spacing,
            out var hit, out var index);

        result.Should().BeTrue();
        hit.Should().Be(A);
        index.Should().Be(0);
    }

    // Row 5: slot 0, mid hit zone (clientX = 8)
    [Fact]
    public void Slot0_MidHitZone_ReturnsHit()
    {
        var items = new[] { A };
        var result = DisplayItemCollection.TryGetItemAtClientPoint(
            items, clientX: 8, IconSize, Spacing,
            out var hit, out var index);

        result.Should().BeTrue();
        hit.Should().Be(A);
        index.Should().Be(0);
    }

    // Row 6: slot 0, right edge of hit zone (clientX = 15)
    [Fact]
    public void Slot0_RightEdgeHitZone_ReturnsHit()
    {
        var items = new[] { A };
        var result = DisplayItemCollection.TryGetItemAtClientPoint(
            items, clientX: 15, IconSize, Spacing,
            out var hit, out var index);

        result.Should().BeTrue();
        hit.Should().Be(A);
        index.Should().Be(0);
    }

    // Row 7: slot 0, gap start (clientX == iconSize = 16)
    [Fact]
    public void Slot0_GapStart_ReturnsFalse()
    {
        var items = new[] { A };
        var result = DisplayItemCollection.TryGetItemAtClientPoint(
            items, clientX: 16, IconSize, Spacing,
            out var hit, out var index);

        result.Should().BeFalse();
        hit.Should().BeNull();
        index.Should().Be(-1);
    }

    // Row 8: slot 0, gap mid (clientX = 18)
    [Fact]
    public void Slot0_GapMid_ReturnsFalse()
    {
        var items = new[] { A };
        var result = DisplayItemCollection.TryGetItemAtClientPoint(
            items, clientX: 18, IconSize, Spacing,
            out var hit, out var index);

        result.Should().BeFalse();
        hit.Should().BeNull();
        index.Should().Be(-1);
    }

    // Row 9: slot 1, hit start (clientX = 20)
    [Fact]
    public void Slot1_HitStart_ReturnsSecondItem()
    {
        var items = new[] { A, B };
        var result = DisplayItemCollection.TryGetItemAtClientPoint(
            items, clientX: 20, IconSize, Spacing,
            out var hit, out var index);

        result.Should().BeTrue();
        hit.Should().Be(B);
        index.Should().Be(1);
    }

    // Row 10: slot 1, hit mid (clientX = 28)
    [Fact]
    public void Slot1_HitMid_ReturnsSecondItem()
    {
        var items = new[] { A, B };
        var result = DisplayItemCollection.TryGetItemAtClientPoint(
            items, clientX: 28, IconSize, Spacing,
            out var hit, out var index);

        result.Should().BeTrue();
        hit.Should().Be(B);
        index.Should().Be(1);
    }

    // Row 11: slot 1, gap (clientX = 36)
    [Fact]
    public void Slot1_Gap_ReturnsFalse()
    {
        var items = new[] { A, B };
        var result = DisplayItemCollection.TryGetItemAtClientPoint(
            items, clientX: 36, IconSize, Spacing,
            out var hit, out var index);

        result.Should().BeFalse();
        hit.Should().BeNull();
        index.Should().Be(-1);
    }

    // Row 12: beyond last item (clientX = 100, single item)
    [Fact]
    public void BeyondLastItem_ReturnsFalse()
    {
        var items = new[] { A };
        var result = DisplayItemCollection.TryGetItemAtClientPoint(
            items, clientX: 100, IconSize, Spacing,
            out var hit, out var index);

        result.Should().BeFalse();
        hit.Should().BeNull();
        index.Should().Be(-1);
    }

    // Row 13: past last item, just into next slot (clientX = 40, two items — slot 2 doesn't exist)
    [Fact]
    public void PastLastItem_JustIntoNextSlot_ReturnsFalse()
    {
        var items = new[] { A, B };
        var result = DisplayItemCollection.TryGetItemAtClientPoint(
            items, clientX: 40, IconSize, Spacing,
            out var hit, out var index);

        result.Should().BeFalse();
        hit.Should().BeNull();
        index.Should().Be(-1);
    }

    // Guard: slot <= 0 (iconSize = 0, spacing = 0) must not divide-by-zero and must return false
    [Fact]
    public void ZeroSlot_ReturnsFalse()
    {
        var items = new[] { A };
        var result = DisplayItemCollection.TryGetItemAtClientPoint(
            items, clientX: 0, iconSize: 0, spacing: 0,
            out var hit, out var index);

        result.Should().BeFalse();
        hit.Should().BeNull();
        index.Should().Be(-1);
    }
}
