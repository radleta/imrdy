using FluentAssertions;
using Imrdy.Core.Wsl;

namespace Imrdy.Core.Tests.Wsl;

public class WslDistroDiscoveryTests : IDisposable
{
    private readonly string _tempRoot;

    public WslDistroDiscoveryTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "imrdy-wsl-discovery", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempRoot);
        WslDistroDiscovery.RootOverride = _tempRoot;
    }

    public void Dispose()
    {
        WslDistroDiscovery.RootOverride = null;
        WslDistroDiscovery.RunningDistrosOverride = null;
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    // Helpers

    private void MakeSessionsDir(string distro, string user)
    {
        var path = Path.Combine(_tempRoot, distro, "home", user, ".imrdy", "sessions");
        Directory.CreateDirectory(path);
    }

    private void MakeDistroRoot(string distro)
    {
        // Creates the distro dir but no home/ — simulates docker-desktop style installs.
        Directory.CreateDirectory(Path.Combine(_tempRoot, distro));
    }

    private static Func<IReadOnlyList<string>> Override(params string[] names)
        => () => names;

    // ── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DiscoverAsync_TwoDistros_ReturnsCorrectNamesAndHomes()
    {
        MakeSessionsDir("Ubuntu-22.04", "radle");
        MakeSessionsDir("Ubuntu-22.04", "other");
        MakeSessionsDir("Ubuntu-24.04", "radle");
        MakeDistroRoot("docker-desktop"); // no home/ — should be excluded
        WslDistroDiscovery.RunningDistrosOverride = Override("Ubuntu-22.04", "Ubuntu-24.04", "docker-desktop");

        var distros = await WslDistroDiscovery.DiscoverAsync();

        distros.Should().HaveCount(2);

        var u22 = distros.Single(d => d.Name == "Ubuntu-22.04");
        u22.LinuxHomes.Should().BeEquivalentTo(["/home/radle", "/home/other"]);

        var u24 = distros.Single(d => d.Name == "Ubuntu-24.04");
        u24.LinuxHomes.Should().ContainSingle().Which.Should().Be("/home/radle");
    }

    [Fact]
    public async Task DiscoverAsync_DockerDesktop_ExcludedBecauseNoHomeDir()
    {
        MakeDistroRoot("docker-desktop");
        WslDistroDiscovery.RunningDistrosOverride = Override("docker-desktop");

        var distros = await WslDistroDiscovery.DiscoverAsync();

        distros.Should().BeEmpty();
    }

    [Fact]
    public async Task DiscoverAsync_UserWithoutImrdySessions_ExcludedFromLinuxHomes()
    {
        // User directory exists but has no .imrdy/sessions — should not appear in LinuxHomes.
        Directory.CreateDirectory(Path.Combine(_tempRoot, "Ubuntu-22.04", "home", "nouser"));
        MakeSessionsDir("Ubuntu-22.04", "radle");
        WslDistroDiscovery.RunningDistrosOverride = Override("Ubuntu-22.04");

        var distros = await WslDistroDiscovery.DiscoverAsync();

        distros.Should().HaveCount(1);
        distros[0].LinuxHomes.Should().ContainSingle().Which.Should().Be("/home/radle");
    }

    // ── Empty / missing root ──────────────────────────────────────────────────

    [Fact]
    public async Task DiscoverAsync_EmptyRoot_ReturnsEmptyList()
    {
        // _tempRoot exists but running override returns no distros.
        WslDistroDiscovery.RunningDistrosOverride = Override();

        var distros = await WslDistroDiscovery.DiscoverAsync();

        distros.Should().BeEmpty();
    }

    [Fact]
    public async Task DiscoverAsync_MissingRoot_ReturnsEmptyList()
    {
        WslDistroDiscovery.RootOverride = Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid());
        WslDistroDiscovery.RunningDistrosOverride = Override("Ubuntu-22.04");

        // The running override returns a name, but the distro dir doesn't exist under the missing root.
        var distros = await WslDistroDiscovery.DiscoverAsync();

        distros.Should().BeEmpty();
    }

    // ── Cancellation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task DiscoverAsync_AlreadyCancelledToken_ThrowsOperationCanceledException()
    {
        WslDistroDiscovery.RunningDistrosOverride = Override();
        var ct = new CancellationToken(canceled: true);

        var act = () => WslDistroDiscovery.DiscoverAsync(ct);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Per-entry failure isolation ───────────────────────────────────────────

    [Fact]
    public async Task DiscoverAsync_MultipleDistros_IterationContinuesPastGoodEntries()
    {
        MakeSessionsDir("Ubuntu-22.04", "radle");
        MakeSessionsDir("Ubuntu-24.04", "radle");
        WslDistroDiscovery.RunningDistrosOverride = Override("Ubuntu-22.04", "Ubuntu-24.04");

        var distros = await WslDistroDiscovery.DiscoverAsync();

        distros.Should().HaveCount(2);
        distros.Should().Contain(d => d.Name == "Ubuntu-22.04");
        distros.Should().Contain(d => d.Name == "Ubuntu-24.04");
    }

    // ── Linux-style home paths ────────────────────────────────────────────────

    [Fact]
    public async Task DiscoverAsync_LinuxHomes_AreLinuxStyleNotUncPaths()
    {
        MakeSessionsDir("Ubuntu-22.04", "radle");
        WslDistroDiscovery.RunningDistrosOverride = Override("Ubuntu-22.04");

        var distros = await WslDistroDiscovery.DiscoverAsync();

        distros.Should().HaveCount(1);
        distros[0].LinuxHomes.Should().ContainSingle().Which.Should().StartWith("/home/");
        distros[0].LinuxHomes[0].Should().NotContain(@"\");
    }

    // ── RunningDistrosOverride ────────────────────────────────────────────────

    [Fact]
    public async Task DiscoverAsync_RunningDistrosOverride_WinsOverSubprocess()
    {
        MakeSessionsDir("Ubuntu-22.04", "radle");
        // Override returns only Ubuntu-22.04; subprocess (if called) would never return this.
        WslDistroDiscovery.RunningDistrosOverride = Override("Ubuntu-22.04");

        var distros = await WslDistroDiscovery.DiscoverAsync();

        distros.Should().HaveCount(1);
        distros[0].Name.Should().Be("Ubuntu-22.04");
    }

    [Fact]
    public async Task DiscoverAsync_RunningDistrosOverrideEmpty_ReturnsEmpty()
    {
        MakeSessionsDir("Ubuntu-22.04", "radle");
        WslDistroDiscovery.RunningDistrosOverride = Override(); // empty — no running distros

        var distros = await WslDistroDiscovery.DiscoverAsync();

        distros.Should().BeEmpty();
    }

    // ── IsValidDistroName ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("Ubuntu-22.04", true)]
    [InlineData("Ubuntu_WSL2", true)]
    [InlineData("debian.wsl", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("..", false)]
    [InlineData("a..b", false)]
    [InlineData("foo/bar", false)]
    [InlineData(@"foo\bar", false)]
    [InlineData(".hidden", false)]
    [InlineData("trailing.", false)]
    public void IsValidDistroName_VariousInputs_ReturnsExpected(string name, bool expected)
    {
        WslDistroDiscovery.IsValidDistroName(name).Should().Be(expected);
    }

    [Fact]
    public void IsValidDistroName_ControlChar_ReturnsFalse()
    {
        WslDistroDiscovery.IsValidDistroName("Ubuntu\x01-22.04").Should().BeFalse();
        WslDistroDiscovery.IsValidDistroName("Ubuntu\x7F-22.04").Should().BeFalse();
    }
}
