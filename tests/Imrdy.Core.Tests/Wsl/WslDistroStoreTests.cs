using FluentAssertions;
using Imrdy.Core.Wsl;

namespace Imrdy.Core.Tests.Wsl;

public class WslDistroStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _filePath;
    private readonly WslDistroStore _store;

    public WslDistroStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "imrdy-wsl-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _filePath = Path.Combine(_tempDir, "wsl-distros.json");
        _store = new WslDistroStore(_filePath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Load / Save ──────────────────────────────────────────────────────────

    [Fact]
    public void Load_MissingFile_ReturnsDefaultConfig()
    {
        var config = _store.Load();

        config.Should().NotBeNull();
        config.WatchAll.Should().BeTrue();
        config.Distros.Should().BeNull();
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaultConfig()
    {
        File.WriteAllText(_filePath, "not json {{{");

        var config = _store.Load();

        config.WatchAll.Should().BeTrue();
        config.Distros.Should().BeNull();
    }

    [Fact]
    public void Save_Load_Roundtrip()
    {
        var now = new DateTimeOffset(2026, 4, 29, 0, 0, 0, TimeSpan.Zero);
        var original = new WslDistroConfig
        {
            WatchAll = false,
            Distros =
            [
                new WslDistroEntry
                {
                    Name = "Ubuntu-22.04",
                    LinuxHomes = ["/home/radle"],
                    Enabled = true,
                    DiscoveredAt = now,
                },
            ],
        };

        _store.Save(original);
        var loaded = _store.Load();

        loaded.WatchAll.Should().BeFalse();
        loaded.Distros.Should().HaveCount(1);
        loaded.Distros![0].Name.Should().Be("Ubuntu-22.04");
        loaded.Distros[0].LinuxHomes.Should().ContainSingle().Which.Should().Be("/home/radle");
        loaded.Distros[0].Enabled.Should().BeTrue();
        loaded.Distros[0].DiscoveredAt.Should().Be(now);
    }

    [Fact]
    public void Save_AtomicWrite_NoTmpFileRemains()
    {
        _store.Save(new WslDistroConfig());

        File.Exists(_filePath + ".tmp").Should().BeFalse();
    }

    // ── Add ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Add_NewEntry_CreatesEntryWithSingleHome()
    {
        _store.Add("Ubuntu-22.04", "/home/foo");

        var config = _store.Load();
        config.Distros.Should().HaveCount(1);
        config.Distros![0].Name.Should().Be("Ubuntu-22.04");
        config.Distros[0].LinuxHomes.Should().ContainSingle().Which.Should().Be("/home/foo");
        config.Distros[0].Enabled.Should().BeTrue(); // WatchAll default is true
    }

    [Fact]
    public void Add_SameNameSameHome_IsNoOp()
    {
        _store.Add("Ubuntu-22.04", "/home/foo");
        _store.Add("Ubuntu-22.04", "/home/foo");

        var config = _store.Load();
        config.Distros.Should().HaveCount(1);
        config.Distros![0].LinuxHomes.Should().HaveCount(1);
    }

    [Fact]
    public void Add_SameNameDifferentHome_AppendsToLinuxHomes()
    {
        _store.Add("Ubuntu-22.04", "/home/foo");
        _store.Add("Ubuntu-22.04", "/home/bar");

        var config = _store.Load();
        config.Distros.Should().HaveCount(1);
        config.Distros![0].LinuxHomes.Should().BeEquivalentTo(["/home/foo", "/home/bar"]);
    }

    [Fact]
    public void Add_NullHome_NewEntry_CreatesEntryWithNullHomes()
    {
        _store.Add("Ubuntu-22.04", null);

        var config = _store.Load();
        config.Distros.Should().HaveCount(1);
        config.Distros![0].LinuxHomes.Should().BeNull();
    }

    [Fact]
    public void Add_NullHome_ExistingEntry_IsNoOp()
    {
        _store.Add("Ubuntu-22.04", "/home/foo");
        _store.Add("Ubuntu-22.04", null);

        var config = _store.Load();
        config.Distros![0].LinuxHomes.Should().HaveCount(1);
    }

    [Fact]
    public void Add_Enabled_InheritsWatchAll()
    {
        _store.SetWatchAll(false);
        _store.Add("Ubuntu-22.04", "/home/foo");

        var config = _store.Load();
        config.Distros![0].Enabled.Should().BeFalse();
    }

    // ── Remove ───────────────────────────────────────────────────────────────

    [Fact]
    public void Remove_ExistingEntry_DeletesIt()
    {
        _store.Add("Ubuntu-22.04", "/home/foo");
        _store.Remove("Ubuntu-22.04");

        var config = _store.Load();
        config.Distros.Should().BeEmpty();
    }

    [Fact]
    public void Remove_NonExistent_IsNoOp()
    {
        _store.Add("Ubuntu-22.04", "/home/foo");
        _store.Remove("Fedora-39"); // doesn't exist

        var config = _store.Load();
        config.Distros.Should().HaveCount(1);
    }

    [Fact]
    public void Remove_CalledTwice_IsIdempotent()
    {
        _store.Add("Ubuntu-22.04", "/home/foo");
        _store.Remove("Ubuntu-22.04");
        _store.Remove("Ubuntu-22.04"); // second remove should not throw

        var config = _store.Load();
        config.Distros.Should().BeEmpty();
    }

    // ── SetEnabled ───────────────────────────────────────────────────────────

    [Fact]
    public void SetEnabled_ExistingEntry_TogglesFlag()
    {
        _store.Add("Ubuntu-22.04", "/home/foo");
        _store.SetEnabled("Ubuntu-22.04", false);

        var config = _store.Load();
        config.Distros![0].Enabled.Should().BeFalse();
    }

    [Fact]
    public void SetEnabled_NonExistent_IsNoOp()
    {
        _store.Add("Ubuntu-22.04", "/home/foo");
        _store.SetEnabled("Fedora-39", false); // doesn't exist

        var config = _store.Load();
        config.Distros![0].Enabled.Should().BeTrue(); // unchanged
    }

    [Fact]
    public void SetEnabled_CalledTwiceWithSameValue_IsIdempotent()
    {
        _store.Add("Ubuntu-22.04", "/home/foo");
        _store.SetEnabled("Ubuntu-22.04", false);
        _store.SetEnabled("Ubuntu-22.04", false);

        var config = _store.Load();
        config.Distros![0].Enabled.Should().BeFalse();
    }

    // ── SetWatchAll ──────────────────────────────────────────────────────────

    [Fact]
    public void SetWatchAll_UpdatesTopLevelFlag()
    {
        _store.SetWatchAll(false);

        var config = _store.Load();
        config.WatchAll.Should().BeFalse();
    }

    [Fact]
    public void SetWatchAll_DoesNotAffectExistingEntries()
    {
        _store.Add("Ubuntu-22.04", "/home/foo");
        _store.SetWatchAll(false);

        var config = _store.Load();
        // Existing entry's Enabled is not retroactively changed
        config.Distros![0].Enabled.Should().BeTrue();
        config.WatchAll.Should().BeFalse();
    }

    // ── Reconcile ────────────────────────────────────────────────────────────

    [Fact]
    public void Reconcile_NewDistro_AddsEntry()
    {
        var discovered = new List<DiscoveredDistro>
        {
            new("Ubuntu-22.04", ["/home/foo"]),
        };

        _store.Reconcile(discovered);

        var config = _store.Load();
        config.Distros.Should().HaveCount(1);
        config.Distros![0].Name.Should().Be("Ubuntu-22.04");
        config.Distros[0].LinuxHomes.Should().ContainSingle().Which.Should().Be("/home/foo");
        config.Distros[0].Enabled.Should().BeTrue(); // WatchAll default true
        config.Distros[0].DiscoveredAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Reconcile_ExistingDistro_MergesLinuxHomesAdditively()
    {
        _store.Add("Ubuntu-22.04", "/home/foo");

        var discovered = new List<DiscoveredDistro>
        {
            new("Ubuntu-22.04", ["/home/bar"]),
        };

        _store.Reconcile(discovered);

        var config = _store.Load();
        config.Distros.Should().HaveCount(1);
        config.Distros![0].LinuxHomes.Should().BeEquivalentTo(["/home/foo", "/home/bar"]);
    }

    [Fact]
    public void Reconcile_IsNonDestructive_PreservesEntriesNotInDiscoveredList()
    {
        _store.Add("Ubuntu-22.04", "/home/foo");
        _store.Add("Fedora-39", "/home/bar");

        var discovered = new List<DiscoveredDistro>
        {
            new("Ubuntu-22.04", []),
        };

        _store.Reconcile(discovered);

        var config = _store.Load();
        config.Distros.Should().HaveCount(2);
        config.Distros!.Should().Contain(e => e.Name == "Fedora-39");
    }

    [Fact]
    public void Reconcile_NewDistro_InheritsWatchAllForEnabled()
    {
        _store.SetWatchAll(false);

        var discovered = new List<DiscoveredDistro>
        {
            new("Ubuntu-22.04", ["/home/foo"]),
        };

        _store.Reconcile(discovered);

        var config = _store.Load();
        config.Distros![0].Enabled.Should().BeFalse();
    }

    [Fact]
    public void Reconcile_DuplicateHome_DoesNotDuplicate()
    {
        _store.Add("Ubuntu-22.04", "/home/foo");

        var discovered = new List<DiscoveredDistro>
        {
            new("Ubuntu-22.04", ["/home/foo"]),
        };

        _store.Reconcile(discovered);

        var config = _store.Load();
        config.Distros![0].LinuxHomes.Should().HaveCount(1);
    }

    // ── Load: path-traversal filtering ───────────────────────────────────────

    [Fact]
    public void Load_DotDotLinuxHome_IsDroppedOnRead()
    {
        // Write a config that contains one good home and one with '..' segments.
        var raw = new WslDistroConfig
        {
            Distros =
            [
                new WslDistroEntry
                {
                    Name = "Ubuntu-22.04",
                    LinuxHomes = ["/home/alice", "/home/alice/../../../Windows/System32"],
                    Enabled = true,
                    DiscoveredAt = DateTimeOffset.UtcNow,
                },
            ],
        };
        _store.Save(raw);

        var loaded = _store.Load();

        loaded.Distros.Should().HaveCount(1);
        loaded.Distros![0].LinuxHomes.Should().ContainSingle()
            .Which.Should().Be("/home/alice");
    }

    [Fact]
    public void Load_DotDotLinuxHome_RoundtripSaveDropsBadValue()
    {
        // Simulate what happens if the JSON is manually edited to add '..' paths
        // and then Save/Load is round-tripped — only the good home survives.
        var raw = new WslDistroConfig
        {
            Distros =
            [
                new WslDistroEntry
                {
                    Name = "Fedora-39",
                    LinuxHomes = ["/../../../etc/passwd", "/home/bob"],
                    Enabled = true,
                    DiscoveredAt = DateTimeOffset.UtcNow,
                },
            ],
        };
        _store.Save(raw);

        var loaded = _store.Load();

        // Only the good home survives Load
        loaded.Distros![0].LinuxHomes.Should().ContainSingle()
            .Which.Should().Be("/home/bob");

        // Re-saving the sanitized config and re-loading preserves only the good home
        _store.Save(loaded);
        var reloaded = _store.Load();
        reloaded.Distros![0].LinuxHomes.Should().ContainSingle()
            .Which.Should().Be("/home/bob");
    }
}
