using FluentAssertions;
using Imrdy.Core.Menus;

namespace Imrdy.Core.Tests.Menus;

public class BuildWslSubmenuTests
{
    private static ControllerMenuState StateWithWsl(WslMenuState wsl) =>
        new()
        {
            Sessions = [],
            Workspaces = [],
            InstalledPacks = [],
            InstalledGraphicsPacks = [],
            Config = new ImrdyConfig(),
            LogPath = @"C:\test\.imrdy\logs\monitor.log",
            Wsl = wsl,
        };

    private static MenuItemModel GetWslSubmenu(WslMenuState wsl)
    {
        var state = StateWithWsl(wsl);
        var items = ControllerMenuModel.Build(state);
        var manage = items.First(i => i.Label == "Manage");
        return manage.Children.First(i => i.Label == "WSL");
    }

    [Fact]
    public void EmptyDistroList_HasWatchAllRescanOpenConfigViewLog_NoPerDistroSeparator()
    {
        var wsl = new WslMenuState
        {
            WatchAll = true,
            Distros = [],
        };

        var wslMenu = GetWslSubmenu(wsl);

        // Watch All + separator + Rescan + Open WSL Config + View WSL Log = 5 items
        // No per-distro separator because there are no per-distro items
        wslMenu.Children.Should().HaveCount(5);
        wslMenu.Children[0].Tag.Should().Be("toggle-wsl-watch-all");
        wslMenu.Children[0].Label.Should().Be("Watch All");
        wslMenu.Children[1].Type.Should().Be(MenuItemType.Separator);
        wslMenu.Children[2].Tag.Should().Be("rescan-distros");
        wslMenu.Children[3].Tag.Should().Be("open-wsl-config");
        wslMenu.Children[4].Tag.Should().Be("view-wsl-log");
    }

    [Fact]
    public void EmptyDistroList_IncludesViewWslLog()
    {
        var wsl = new WslMenuState
        {
            WatchAll = true,
            Distros = [],
        };

        var wslMenu = GetWslSubmenu(wsl);

        // The trailing separator + action items
        var tags = wslMenu.Children.Select(c => c.Tag).ToList();
        tags.Should().Contain("view-wsl-log");
    }

    [Fact]
    public void WatchAllFalse_PerDistroItemsAreDisabled()
    {
        var wsl = new WslMenuState
        {
            WatchAll = false,
            Distros =
            [
                new WslDistroMenuEntry
                {
                    Name = "Ubuntu-22.04",
                    Enabled = true,
                    IsRunning = false,
                    SessionCount = 0,
                },
            ],
        };

        var wslMenu = GetWslSubmenu(wsl);

        var distroItem = wslMenu.Children.First(c => c.Tag == "toggle-wsl-distro:Ubuntu-22.04");
        distroItem.Enabled.Should().BeFalse();
    }

    [Fact]
    public void RunningDistro_PluralSessions_LabelFormatted()
    {
        var wsl = new WslMenuState
        {
            WatchAll = true,
            Distros =
            [
                new WslDistroMenuEntry
                {
                    Name = "Ubuntu-22.04",
                    Enabled = true,
                    IsRunning = true,
                    SessionCount = 2,
                },
            ],
        };

        var wslMenu = GetWslSubmenu(wsl);

        var distroItem = wslMenu.Children.First(c => c.Tag == "toggle-wsl-distro:Ubuntu-22.04");
        distroItem.Label.Should().Be("Ubuntu-22.04   (running · 2 sessions)");
    }

    [Fact]
    public void RunningDistro_SingleSession_LabelSingular()
    {
        var wsl = new WslMenuState
        {
            WatchAll = true,
            Distros =
            [
                new WslDistroMenuEntry
                {
                    Name = "Ubuntu-22.04",
                    Enabled = true,
                    IsRunning = true,
                    SessionCount = 1,
                },
            ],
        };

        var wslMenu = GetWslSubmenu(wsl);

        var distroItem = wslMenu.Children.First(c => c.Tag == "toggle-wsl-distro:Ubuntu-22.04");
        distroItem.Label.Should().Be("Ubuntu-22.04   (running · 1 session)");
    }

    [Fact]
    public void StoppedDistro_LabelShowsStopped()
    {
        var wsl = new WslMenuState
        {
            WatchAll = true,
            Distros =
            [
                new WslDistroMenuEntry
                {
                    Name = "Ubuntu-22.04",
                    Enabled = true,
                    IsRunning = false,
                    SessionCount = 0,
                },
            ],
        };

        var wslMenu = GetWslSubmenu(wsl);

        var distroItem = wslMenu.Children.First(c => c.Tag == "toggle-wsl-distro:Ubuntu-22.04");
        distroItem.Label.Should().Be("Ubuntu-22.04   (stopped)");
    }

    [Fact]
    public void PerDistroItem_TagUsesColonSeparator()
    {
        var wsl = new WslMenuState
        {
            WatchAll = true,
            Distros =
            [
                new WslDistroMenuEntry
                {
                    Name = "Ubuntu-22.04",
                    Enabled = true,
                    IsRunning = false,
                    SessionCount = 0,
                },
            ],
        };

        var wslMenu = GetWslSubmenu(wsl);

        var distroItem = wslMenu.Children.FirstOrDefault(c =>
            c.Tag?.StartsWith("toggle-wsl-distro:") == true);
        distroItem.Should().NotBeNull();
        distroItem!.Tag.Should().Be("toggle-wsl-distro:Ubuntu-22.04");
    }

    [Fact]
    public void WslNull_ManageSubmenuHasNoWslEntry()
    {
        var state = new ControllerMenuState
        {
            Sessions = [],
            Workspaces = [],
            InstalledPacks = [],
            InstalledGraphicsPacks = [],
            Config = new ImrdyConfig(),
            LogPath = @"C:\test\.imrdy\logs\monitor.log",
            Wsl = null,
        };

        var items = ControllerMenuModel.Build(state);
        var manage = items.First(i => i.Label == "Manage");

        manage.Children.Should().NotContain(c => c.Label == "WSL");
    }
}
