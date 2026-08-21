using FluentAssertions;
using Imrdy.Windows.Menus;
using Xunit;

namespace Imrdy.Windows.Tests.Menus;

/// <summary>
/// Unit tests for <see cref="MenuOpeningPolicy.ShouldClearCancel"/> — the pure decision
/// logic behind the Step 08 fix for the "first right-click on a never-before-opened menu is
/// eaten" defect. Deliberately does not touch a live <see cref="System.Windows.Forms.ContextMenuStrip"/>:
/// this is the part of the fix that can (and should) be verified without a WinForms message
/// loop. See <see cref="Imrdy.Windows.Tests.Menus.MenuOpeningEndToEndTests"/> for the
/// live-ContextMenuStrip regression coverage.
/// </summary>
public class MenuOpeningPolicyTests
{
    [Fact]
    public void ShouldClearCancel_ItemsPresent_ReturnsTrue()
    {
        // The common case: the Opening rebuild populated the menu, so WinForms'
        // pre-set e.Cancel = true (based on the zero-item count that existed before the
        // handler ran) must be cleared or the menu will never display.
        MenuOpeningPolicy.ShouldClearCancel(itemCount: 15).Should().BeTrue();
    }

    [Fact]
    public void ShouldClearCancel_ExactlyOneItem_ReturnsTrue()
    {
        MenuOpeningPolicy.ShouldClearCancel(itemCount: 1).Should().BeTrue();
    }

    [Fact]
    public void ShouldClearCancel_ZeroItems_ReturnsFalse()
    {
        // The rebuild legitimately produced no items — WinForms' original refusal to show
        // an empty menu is correct here and must NOT be overridden.
        MenuOpeningPolicy.ShouldClearCancel(itemCount: 0).Should().BeFalse();
    }
}
