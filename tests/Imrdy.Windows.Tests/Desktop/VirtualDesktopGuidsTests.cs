using FluentAssertions;
using Imrdy.Windows.Desktop;
using Xunit;

namespace Imrdy.Windows.Tests.Desktop;

/// <summary>
/// Pins the IVirtualDesktopManagerInternal candidate table. A wrong FindDesktopSlot is not a
/// harmless miss: on 53F5CA0B slot 13 is RemoveDesktop, so each IID must carry its own layout.
/// </summary>
public class VirtualDesktopGuidsTests
{
    private static readonly Guid Win11Current = new("53f5ca0b-158f-4124-900c-057158060b27");

    [Fact]
    public void GetInternalLayouts_Windows10_ReturnsOnlyWin10Layout()
    {
        VirtualDesktopGuids.GetInternalLayouts(19045).Should().ContainSingle()
            .Which.Should().Be(new InternalLayout(new Guid("f31574d6-b682-4cdc-bd56-1827860abec6"), false, 12));
    }

    [Theory]
    [InlineData(22000)]
    [InlineData(22631)]
    [InlineData(26100)]
    [InlineData(26200)]
    public void GetInternalLayouts_Windows11_TriesCurrentIidFirstWithItsOwnSlots(int build)
    {
        var layouts = VirtualDesktopGuids.GetInternalLayouts(build);

        layouts[0].Should().Be(new InternalLayout(Win11Current, HasMonitorArg: false, FindDesktopSlot: 14));
        layouts.Skip(1).Should().OnlyContain(l => l.HasMonitorArg && l.FindDesktopSlot == 13);
        layouts.Select(l => l.Iid).Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData(17763)]
    [InlineData(20000)]
    public void GetInternalLayouts_UnknownBuild_ReturnsEmpty(int build)
    {
        VirtualDesktopGuids.GetInternalLayouts(build).Should().BeEmpty();
    }
}
