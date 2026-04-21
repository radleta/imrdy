using System.Drawing;
using System.Windows.Forms;
using FluentAssertions;
using Imrdy.Windows.Interaction;
using Xunit;

namespace Imrdy.Integration.Tests;

[Trait("Category", "Integration")]
public class MenuAnchorTests
{
    [Fact]
    public void AtTrayIcon_CarriesIconAndNoOwner()
    {
        using var icon = new NotifyIcon();

        var anchor = MenuAnchor.AtTrayIcon(icon);

        anchor.TrayIcon.Should().BeSameAs(icon);
        anchor.Owner.Should().BeNull();
        anchor.Location.Should().Be(default(Point));
    }

    [Fact]
    public void AtControl_CarriesOwnerAndLocationAndNoIcon()
    {
        using var owner = new Control();
        var point = new Point(10, 20);

        var anchor = MenuAnchor.AtControl(owner, point);

        anchor.Owner.Should().BeSameAs(owner);
        anchor.Location.Should().Be(point);
        anchor.TrayIcon.Should().BeNull();
    }

    [Fact]
    public void Default_HasNeitherTrayIconNorOwner()
    {
        // Default(MenuAnchor) is a guard case: ShowContextMenuAt falls through both branches.
        var anchor = default(MenuAnchor);

        anchor.TrayIcon.Should().BeNull();
        anchor.Owner.Should().BeNull();
    }
}
