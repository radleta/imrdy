using FluentAssertions;
using Imrdy.Core.Wsl;
using Imrdy.Windows;
using Xunit;

namespace Imrdy.Windows.Tests.TrayApp;

/// <summary>
/// Unit tests for WSL watcher reconciliation logic.
/// Tests target <see cref="WslWatcherReconciler"/> — the pure static helper extracted
/// from TrayApp so full WinForms instantiation is not required.
/// </summary>
public class WslWatcherLifecycleTests
{
    // ---- ComputeDelta ----

    [Fact]
    public void ComputeDelta_EmptyCurrentAndNoDistros_ProducesEmptyDelta()
    {
        WslWatcherReconciler.ComputeDelta(
            [],
            Array.Empty<DiscoveredDistro>(),
            new WslDistroConfig(),
            out var toArm,
            out var toDisarm);

        toArm.Should().BeEmpty();
        toDisarm.Should().BeEmpty();
    }

    [Fact]
    public void ComputeDelta_WatchAllTrue_ArmsAllDiscoveredHomes()
    {
        var distros = new List<DiscoveredDistro>
        {
            new("Ubuntu-22.04", ["/home/alice", "/home/bob"]),
            new("Ubuntu-24.04", ["/home/carol"]),
        };
        var config = new WslDistroConfig { WatchAll = true };

        WslWatcherReconciler.ComputeDelta(
            [],
            distros,
            config,
            out var toArm,
            out var toDisarm);

        toArm.Should().BeEquivalentTo(new[]
        {
            ("Ubuntu-22.04", "/home/alice"),
            ("Ubuntu-22.04", "/home/bob"),
            ("Ubuntu-24.04", "/home/carol"),
        });
        toDisarm.Should().BeEmpty();
    }

    [Fact]
    public void ComputeDelta_WatchAllFalse_OnlyArmsEnabledEntries()
    {
        var distros = new List<DiscoveredDistro>
        {
            new("Ubuntu-22.04", ["/home/alice"]),
            new("Ubuntu-24.04", ["/home/carol"]),
        };
        var config = new WslDistroConfig
        {
            WatchAll = false,
            Distros =
            [
                new WslDistroEntry { Name = "Ubuntu-22.04", Enabled = true },
                new WslDistroEntry { Name = "Ubuntu-24.04", Enabled = false },
            ],
        };

        WslWatcherReconciler.ComputeDelta(
            [],
            distros,
            config,
            out var toArm,
            out var toDisarm);

        toArm.Should().BeEquivalentTo(new[] { ("Ubuntu-22.04", "/home/alice") });
        toDisarm.Should().BeEmpty();
    }

    [Fact]
    public void ComputeDelta_DistroStops_ExistingKeyGoesToDisarm()
    {
        var currentKeys = new[] { ("Ubuntu-22.04", "/home/alice") };
        var distros = Array.Empty<DiscoveredDistro>();
        var config = new WslDistroConfig { WatchAll = true };

        WslWatcherReconciler.ComputeDelta(
            currentKeys,
            distros,
            config,
            out var toArm,
            out var toDisarm);

        toArm.Should().BeEmpty();
        toDisarm.Should().BeEquivalentTo(new[] { ("Ubuntu-22.04", "/home/alice") });
    }

    [Fact]
    public void ComputeDelta_AlreadyArmed_NotInToArm()
    {
        var currentKeys = new[] { ("Ubuntu-22.04", "/home/alice") };
        var distros = new List<DiscoveredDistro>
        {
            new("Ubuntu-22.04", ["/home/alice", "/home/bob"]),
        };
        var config = new WslDistroConfig { WatchAll = true };

        WslWatcherReconciler.ComputeDelta(
            currentKeys,
            distros,
            config,
            out var toArm,
            out var toDisarm);

        toArm.Should().BeEquivalentTo(new[] { ("Ubuntu-22.04", "/home/bob") });
        toDisarm.Should().BeEmpty();
    }

    [Fact]
    public void ComputeDelta_WatchAllFlippedToFalse_DisablesArmedDistro()
    {
        var currentKeys = new[] { ("Ubuntu-22.04", "/home/alice") };
        var distros = new List<DiscoveredDistro>
        {
            new("Ubuntu-22.04", ["/home/alice"]),
        };
        var config = new WslDistroConfig
        {
            WatchAll = false,
            Distros = [new WslDistroEntry { Name = "Ubuntu-22.04", Enabled = false }],
        };

        WslWatcherReconciler.ComputeDelta(
            currentKeys,
            distros,
            config,
            out var toArm,
            out var toDisarm);

        toArm.Should().BeEmpty();
        toDisarm.Should().BeEquivalentTo(new[] { ("Ubuntu-22.04", "/home/alice") });
    }

    // ---- BuildUncPath ----

    [Fact]
    public void BuildUncPath_ProducesCorrectUncPath()
    {
        var result = WslWatcherReconciler.BuildUncPath("Ubuntu-22.04", "/home/alice");

        result.Should().Be(@"\\wsl.localhost\Ubuntu-22.04\home\alice\.imrdy\sessions");
    }

    [Fact]
    public void BuildUncPath_NestedHome_ProducesCorrectUncPath()
    {
        var result = WslWatcherReconciler.BuildUncPath("Debian", "/home/user");

        result.Should().Be(@"\\wsl.localhost\Debian\home\user\.imrdy\sessions");
    }
}
