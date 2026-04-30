using FluentAssertions;
using Imrdy.Core.Wsl;
using Imrdy.Integration.Tests.Helpers;
using Xunit;

namespace Imrdy.Integration.Tests;

/// <summary>
/// Integration tests for WSL discovery via a temp directory mock of the UNC root.
/// Uses <see cref="WslDistroDiscovery.RootOverride"/> to substitute a temp directory
/// for <c>\\wsl.localhost\</c> so the test runs without an actual WSL distro.
/// </summary>
[Trait("Category", "Integration")]
public class WslDiscoveryIntegrationTests : IDisposable
{
    private readonly TempDirectoryFixture _temp = new();

    public void Dispose()
    {
        WslDistroDiscovery.RootOverride = null;
        _temp.Dispose();
    }

    private string SetupDistro(string distroName, string userName)
    {
        // Mirrors \\wsl.localhost\<distro>\home\<user>\.imrdy\sessions
        var sessionsDir = Path.Combine(_temp.Path, distroName, "home", userName, ".imrdy", "sessions");
        Directory.CreateDirectory(sessionsDir);
        return sessionsDir;
    }

    [Fact]
    public async Task DiscoverAsync_FindsDistroWithImrdySessions()
    {
        SetupDistro("Ubuntu-22.04", "alice");
        WslDistroDiscovery.RootOverride = _temp.Path + Path.DirectorySeparatorChar;

        var result = await WslDistroDiscovery.DiscoverAsync(CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Ubuntu-22.04");
        result[0].LinuxHomes.Should().BeEquivalentTo(["/home/alice"]);
    }

    [Fact]
    public async Task DiscoverAsync_MultipleUsers_ReturnsAllHomes()
    {
        SetupDistro("Ubuntu-22.04", "alice");
        SetupDistro("Ubuntu-22.04", "bob");
        WslDistroDiscovery.RootOverride = _temp.Path + Path.DirectorySeparatorChar;

        var result = await WslDistroDiscovery.DiscoverAsync(CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Ubuntu-22.04");
        result[0].LinuxHomes.Should().BeEquivalentTo(["/home/alice", "/home/bob"]);
    }

    [Fact]
    public async Task DiscoverAsync_MultipleDistros_ReturnsAll()
    {
        SetupDistro("Ubuntu-22.04", "alice");
        SetupDistro("Debian", "carol");
        WslDistroDiscovery.RootOverride = _temp.Path + Path.DirectorySeparatorChar;

        var result = await WslDistroDiscovery.DiscoverAsync(CancellationToken.None);

        result.Should().HaveCount(2);
        result.Select(d => d.Name).Should().BeEquivalentTo(["Ubuntu-22.04", "Debian"]);
    }

    [Fact]
    public async Task DiscoverAsync_DistroWithNoImrdyDir_IsSkipped()
    {
        // Distro exists but no ~/.imrdy/sessions
        Directory.CreateDirectory(Path.Combine(_temp.Path, "NoImrdy", "home", "user"));
        SetupDistro("Ubuntu-22.04", "alice");
        WslDistroDiscovery.RootOverride = _temp.Path + Path.DirectorySeparatorChar;

        var result = await WslDistroDiscovery.DiscoverAsync(CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Ubuntu-22.04");
    }

    [Fact]
    public async Task DiscoverAsync_EmptyRoot_ReturnsEmpty()
    {
        WslDistroDiscovery.RootOverride = _temp.Path + Path.DirectorySeparatorChar;

        var result = await WslDistroDiscovery.DiscoverAsync(CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task WslWatcherReconciler_ComputeDelta_IntegrationRoundtrip()
    {
        // Validates the reconciler with real DiscoveredDistro instances.
        SetupDistro("Ubuntu-22.04", "alice");
        WslDistroDiscovery.RootOverride = _temp.Path + Path.DirectorySeparatorChar;

        var distros = await WslDistroDiscovery.DiscoverAsync(CancellationToken.None);
        var config = new WslDistroConfig { WatchAll = true };

        Imrdy.Windows.WslWatcherReconciler.ComputeDelta(
            [],
            distros,
            config,
            out var toArm,
            out var toDisarm);

        toArm.Should().ContainSingle().Which.Should().Be(("Ubuntu-22.04", "/home/alice"));
        toDisarm.Should().BeEmpty();

        // UNC path should be well-formed
        var uncPath = Imrdy.Windows.WslWatcherReconciler.BuildUncPath("Ubuntu-22.04", "/home/alice");
        uncPath.Should().Be(@"\\wsl.localhost\Ubuntu-22.04\home\alice\.imrdy\sessions");
    }
}
