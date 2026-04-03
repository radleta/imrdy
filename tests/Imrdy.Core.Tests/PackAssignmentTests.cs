using Imrdy.Core.Sound;
using FluentAssertions;

namespace Imrdy.Core.Tests;

public class PackAssignmentTests
{
    private static PackLoader.LoadedPack MakePack(string name, bool hasWavs = true)
    {
        var wavFiles = hasWavs
            ? new Dictionary<SoundEvent, string[]> { [SoundEvent.SessionStart] = ["s.wav"] }
            : new Dictionary<SoundEvent, string[]>();

        return new PackLoader.LoadedPack(
            name, $"Desc {name}", "1.0", $"/packs/{name}",
            new PackJson { Name = name }, wavFiles);
    }

    private static readonly IReadOnlyList<PackLoader.LoadedPack> TwoPacks =
        [MakePack("assistant"), MakePack("retro")];

    [Fact]
    public void Priority1_StateFileOverride_TakesPrecedence()
    {
        var assignment = new PackAssignment(TwoPacks, new SoundConfig { Default = "retro" }, "assistant");

        assignment.Resolve("retro", "some-project").Should().Be("retro");
    }

    [Fact]
    public void Priority2_ConfigProjectMapping()
    {
        var config = new SoundConfig
        {
            ProjectMappings = new() { ["my-project"] = "retro" }
        };
        var assignment = new PackAssignment(TwoPacks, config);

        assignment.Resolve(null, "my-project").Should().Be("retro");
    }

    [Fact]
    public void Priority3_ConfigDefault()
    {
        var config = new SoundConfig { Default = "assistant" };
        var assignment = new PackAssignment(TwoPacks, config);

        assignment.Resolve(null, "other-project").Should().Be("assistant");
    }

    [Fact]
    public void Priority4_CliDefault()
    {
        var assignment = new PackAssignment(TwoPacks, cliDefault: "retro");

        assignment.Resolve(null, null).Should().Be("retro");
    }

    [Fact]
    public void Priority5_AutoDetect_SinglePackWithWavs()
    {
        var packs = new[] { MakePack("only-one") };
        var assignment = new PackAssignment(packs);

        assignment.Resolve(null, null).Should().Be("only-one");
    }

    [Fact]
    public void Priority5_AutoDetect_MultiplePacks_ReturnsNull()
    {
        var assignment = new PackAssignment(TwoPacks);

        assignment.Resolve(null, null).Should().BeNull();
    }

    [Fact]
    public void StateFileOverride_NonExistentPack_FallsThrough()
    {
        var config = new SoundConfig { Default = "assistant" };
        var assignment = new PackAssignment(TwoPacks, config);

        assignment.Resolve("nonexistent", "some-project").Should().Be("assistant");
    }

    [Fact]
    public void ProjectMapping_NonExistentPack_FallsThrough()
    {
        var config = new SoundConfig
        {
            Default = "retro",
            ProjectMappings = new() { ["my-project"] = "deleted-pack" }
        };
        var assignment = new PackAssignment(TwoPacks, config);

        assignment.Resolve(null, "my-project").Should().Be("retro");
    }

    [Fact]
    public void NoPacks_ReturnsNull()
    {
        var assignment = new PackAssignment(Array.Empty<PackLoader.LoadedPack>());
        assignment.Resolve(null, null).Should().BeNull();
    }

    [Fact]
    public void PriorityChain_FullFallthrough()
    {
        // No state override, no project mapping, no default, no CLI, multiple packs
        var assignment = new PackAssignment(TwoPacks);
        assignment.Resolve(null, null).Should().BeNull();
    }

    [Fact]
    public void CaseInsensitive_PackNameMatching()
    {
        var assignment = new PackAssignment(TwoPacks);
        assignment.Resolve("ASSISTANT", null).Should().Be("ASSISTANT");
    }

    [Fact]
    public void AutoDetect_SkipsPacksWithNoWavs()
    {
        var packs = new[] { MakePack("empty", hasWavs: false), MakePack("has-wavs") };
        var assignment = new PackAssignment(packs);

        assignment.Resolve(null, null).Should().Be("has-wavs");
    }

    [Fact]
    public void NullProjectMappings_DoesNotThrow()
    {
        // JSON deserialization can produce null ProjectMappings when config has "projectMappings": null
        var config = new SoundConfig { Default = "assistant", ProjectMappings = null! };
        var assignment = new PackAssignment(TwoPacks, config);

        assignment.Resolve(null, "some-project").Should().Be("assistant");
    }

    [Fact]
    public void NullProjectMappings_WithProject_FallsToDefault()
    {
        var config = new SoundConfig { Default = "retro", ProjectMappings = null! };
        var assignment = new PackAssignment(TwoPacks, config);

        assignment.Resolve(null, "my-project").Should().Be("retro");
    }
}

public class PackAssignmentLoadConfigTests : IDisposable
{
    private readonly string _tempDir;

    public PackAssignmentLoadConfigTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "imrdy-config-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void LoadConfig_ValidFile_ReturnsParsedConfig()
    {
        var path = Path.Combine(_tempDir, "config.json");
        File.WriteAllText(path, """{"default": "assistant", "projectMappings": {"my-proj": "retro"}}""");

        var config = PackAssignment.LoadConfig(path);

        config.Default.Should().Be("assistant");
        config.ProjectMappings.Should().ContainKey("my-proj");
        config.ProjectMappings["my-proj"].Should().Be("retro");
    }

    [Fact]
    public void LoadConfig_MissingFile_ReturnsEmptyConfig()
    {
        var config = PackAssignment.LoadConfig(Path.Combine(_tempDir, "nope.json"));

        config.Default.Should().BeNull();
        config.ProjectMappings.Should().BeEmpty();
    }

    [Fact]
    public void LoadConfig_CorruptJson_ReturnsEmptyConfig()
    {
        var path = Path.Combine(_tempDir, "bad.json");
        File.WriteAllText(path, "not json");

        var config = PackAssignment.LoadConfig(path);

        config.Default.Should().BeNull();
        config.ProjectMappings.Should().BeEmpty();
    }

    [Fact]
    public void LoadConfig_NullProjectMappings_DeserializesWithoutError()
    {
        var path = Path.Combine(_tempDir, "config.json");
        File.WriteAllText(path, """{"default":"assistant","projectMappings":null,"soundEnabled":true}""");

        var config = PackAssignment.LoadConfig(path);

        config.Default.Should().Be("assistant");
        config.SoundEnabled.Should().BeTrue();
        // ProjectMappings may be null from JSON — PackAssignment.Resolve handles this
    }
}
