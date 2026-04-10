using System.Text.Json;
using FluentAssertions;
using Imrdy.Core.Graphics;

namespace Imrdy.Core.Tests.Graphics;

public class GraphicsPackLoaderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly GraphicsPackLoader _loader;

    public GraphicsPackLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "imrdy-graphics-pack-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _loader = new GraphicsPackLoader();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private string CreatePackDir(
        string name,
        string format = "svg",
        Dictionary<string, string>? states = null,
        bool createStateFiles = true,
        string? overrideName = null)
    {
        var packDir = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(packDir);

        var statesDict = states?.ToDictionary(
            s => s.Key,
            s => (object)new { file = s.Value }) ?? new Dictionary<string, object>();

        var packJson = new
        {
            name = overrideName ?? name,
            format,
            version = "1.0.0",
            license = "MIT",
            states = statesDict,
        };

        File.WriteAllText(
            Path.Combine(packDir, "pack.json"),
            JsonSerializer.Serialize(packJson));

        if (createStateFiles && states is not null)
        {
            foreach (var (_, relativePath) in states)
            {
                var fullPath = Path.Combine(packDir, relativePath);
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(fullPath, "<svg/>");
            }
        }

        return packDir;
    }

    [Fact]
    public void LoadPacks_WithMissingRoot_ReturnsEmpty()
    {
        var missing = Path.Combine(_tempDir, "does-not-exist");
        _loader.LoadPacks(missing).Should().BeEmpty();
    }

    [Fact]
    public void LoadPacks_WithValidPack_LoadsSuccessfully()
    {
        CreatePackDir("dev-test", states: new()
        {
            ["idle"] = "idle.svg",
            ["busy"] = "busy.svg",
        });

        var packs = _loader.LoadPacks(_tempDir);

        packs.Should().HaveCount(1);
        var pack = packs.Single();
        pack.Name.Should().Be("dev-test");
        pack.Format.Should().Be("svg");
        pack.Version.Should().Be("1.0.0");
        pack.License.Should().Be("MIT");
        pack.StateFilePaths.Should().ContainKey("idle");
        pack.StateFilePaths.Should().ContainKey("busy");
        pack.StateFilePaths["idle"].Should().EndWith("idle.svg");
        File.Exists(pack.StateFilePaths["idle"]).Should().BeTrue();
    }

    [Fact]
    public void LoadPack_WithMissingPackJson_ReturnsNull()
    {
        var packDir = Path.Combine(_tempDir, "empty");
        Directory.CreateDirectory(packDir);

        _loader.LoadPack(packDir, Path.Combine(packDir, "pack.json")).Should().BeNull();
    }

    [Fact]
    public void LoadPack_WithMalformedJson_ReturnsNull()
    {
        var packDir = Path.Combine(_tempDir, "broken");
        Directory.CreateDirectory(packDir);
        var packJsonPath = Path.Combine(packDir, "pack.json");
        File.WriteAllText(packJsonPath, "{ this is not valid json");

        _loader.LoadPack(packDir, packJsonPath).Should().BeNull();
    }

    [Fact]
    public void LoadPack_WithEmptyName_ReturnsNull()
    {
        var packDir = Path.Combine(_tempDir, "no-name");
        Directory.CreateDirectory(packDir);
        var packJsonPath = Path.Combine(packDir, "pack.json");
        File.WriteAllText(
            packJsonPath,
            """{"name": "", "format": "svg", "version": "1.0.0", "license": "MIT", "states": {"idle": {"file": "idle.svg"}}}""");
        File.WriteAllText(Path.Combine(packDir, "idle.svg"), "<svg/>");

        _loader.LoadPack(packDir, packJsonPath).Should().BeNull();
    }

    [Fact]
    public void LoadPack_WithMissingStateFile_ReturnsNull()
    {
        CreatePackDir(
            "missing-file",
            states: new() { ["idle"] = "idle.svg" },
            createStateFiles: false);

        var packs = _loader.LoadPacks(_tempDir);
        packs.Should().BeEmpty();
    }

    [Fact]
    public void LoadPack_WithPathEscapeAttempt_RejectsState()
    {
        // Create an "evil" file outside the pack directory but within _tempDir
        var evilFile = Path.Combine(_tempDir, "evil.svg");
        File.WriteAllText(evilFile, "<svg/>");

        var packDir = Path.Combine(_tempDir, "attacker");
        Directory.CreateDirectory(packDir);
        var packJsonPath = Path.Combine(packDir, "pack.json");
        File.WriteAllText(
            packJsonPath,
            """{"name": "attacker", "format": "svg", "version": "1.0.0", "license": "MIT", "states": {"idle": {"file": "../evil.svg"}}}""");

        _loader.LoadPack(packDir, packJsonPath).Should().BeNull();
    }

    [Fact]
    public void LoadPack_WithSiblingDirectoryBypassAttempt_RejectsState()
    {
        // Pack name is "attacker"; sibling directory is "attacker-evil".
        // Without a trailing separator on the containment prefix, "attacker-evil" starts
        // with "attacker" and the check passes incorrectly.
        var siblingDir = Path.Combine(_tempDir, "attacker-evil");
        Directory.CreateDirectory(siblingDir);
        var evilFile = Path.Combine(siblingDir, "evil.svg");
        File.WriteAllText(evilFile, "<svg/>");

        var packDir = Path.Combine(_tempDir, "attacker");
        Directory.CreateDirectory(packDir);
        var packJsonPath = Path.Combine(packDir, "pack.json");
        File.WriteAllText(
            packJsonPath,
            """{"name": "attacker", "format": "svg", "version": "1.0.0", "license": "MIT", "states": {"idle": {"file": "../attacker-evil/evil.svg"}}}""");

        _loader.LoadPack(packDir, packJsonPath).Should().BeNull();
    }

    [Fact]
    public void LoadPack_WithInvalidFormat_ReturnsNull()
    {
        CreatePackDir(
            "weird-format",
            format: "gif",
            states: new() { ["idle"] = "idle.svg" });

        var packs = _loader.LoadPacks(_tempDir);
        packs.Should().BeEmpty();
    }
}
