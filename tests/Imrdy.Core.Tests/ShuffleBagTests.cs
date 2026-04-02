using Imrdy.Core.Sound;
using FluentAssertions;

namespace Imrdy.Core.Tests;

public class ShuffleBagTests
{
    [Fact]
    public void Draw_ReturnsAllItemsBeforeRepeating()
    {
        var bag = new ShuffleBag<int>([1, 2, 3], new Random(42));
        var drawn = new List<int>();

        for (var i = 0; i < 3; i++)
        {
            drawn.Add(bag.Draw()!);
        }

        drawn.Should().BeEquivalentTo([1, 2, 3]);
    }

    [Fact]
    public void Draw_NoConsecutiveDuplicatesAcrossRefills()
    {
        var items = new[] { 1, 2, 3, 4, 5 };
        var bag = new ShuffleBag<int>(items, new Random(42));

        int? previous = null;
        for (var i = 0; i < 100; i++)
        {
            var current = bag.Draw();
            if (previous is not null)
            {
                current.Should().NotBe(previous, $"consecutive duplicate at draw {i}");
            }
            previous = current;
        }
    }

    [Fact]
    public void Draw_EmptyBag_ReturnsDefault()
    {
        var bag = new ShuffleBag<string>(Array.Empty<string>());
        bag.Draw().Should().BeNull();
    }

    [Fact]
    public void Draw_SingleItem_AlwaysReturnsSameItem()
    {
        var bag = new ShuffleBag<string>(["only"]);

        for (var i = 0; i < 5; i++)
        {
            bag.Draw().Should().Be("only");
        }
    }

    [Fact]
    public void Draw_TwoItems_AlternatesAfterRefill()
    {
        var bag = new ShuffleBag<int>([1, 2], new Random(42));
        var drawn = new List<int>();

        for (var i = 0; i < 10; i++)
        {
            drawn.Add(bag.Draw()!);
        }

        // Should never have consecutive duplicates
        for (var i = 1; i < drawn.Count; i++)
        {
            drawn[i].Should().NotBe(drawn[i - 1], $"consecutive duplicate at position {i}");
        }
    }

    [Fact]
    public void Count_ReturnsOriginalItemCount()
    {
        var bag = new ShuffleBag<int>([1, 2, 3]);
        bag.Count.Should().Be(3);

        // Drawing doesn't change Count
        bag.Draw();
        bag.Count.Should().Be(3);
    }

    [Fact]
    public void Draw_StringItems_WorksCorrectly()
    {
        var bag = new ShuffleBag<string>(["a.wav", "b.wav", "c.wav"], new Random(42));
        var drawn = new HashSet<string>();

        for (var i = 0; i < 3; i++)
        {
            drawn.Add(bag.Draw()!);
        }

        drawn.Should().BeEquivalentTo(["a.wav", "b.wav", "c.wav"]);
    }

    [Fact]
    public void Draw_MultipleFullCycles_AllItemsAppearEqually()
    {
        var bag = new ShuffleBag<int>([1, 2, 3], new Random(42));
        var counts = new Dictionary<int, int> { [1] = 0, [2] = 0, [3] = 0 };

        for (var i = 0; i < 30; i++)
        {
            counts[bag.Draw()!]++;
        }

        // After 10 full cycles, each item should appear exactly 10 times
        counts[1].Should().Be(10);
        counts[2].Should().Be(10);
        counts[3].Should().Be(10);
    }
}
