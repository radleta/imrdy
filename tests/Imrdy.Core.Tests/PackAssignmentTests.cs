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
        var assignment = new PackAssignment(TwoPacks, new SoundConfig { DefaultPack = "retro" }, "assistant");

        assignment.Resolve("retro", "some-project").Should().Be("retro");
    }

    [Fact]
    public void Priority2_ConfigProjectMapping()
    {
        var config = new SoundConfig
        {
            Projects = new() { ["my-project"] = "retro" }
        };
        var assignment = new PackAssignment(TwoPacks, config);

        assignment.Resolve(null, "my-project").Should().Be("retro");
    }

    [Fact]
    public void Priority3_ConfigDefault()
    {
        var config = new SoundConfig { DefaultPack = "assistant" };
        var assignment = new PackAssignment(TwoPacks, config);

        assignment.Resolve(null, "other-project").Should().Be("assistant");
    }

    [Fact]
    public void Priority4_CliDefault()
    {
        var config = new SoundConfig { DefaultPack = "" };
        var assignment = new PackAssignment(TwoPacks, config, cliDefault: "retro");

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
        var config = new SoundConfig { DefaultPack = "" };
        var assignment = new PackAssignment(TwoPacks, config);

        assignment.Resolve(null, null).Should().BeNull();
    }

    [Fact]
    public void StateFileOverride_NonExistentPack_FallsThrough()
    {
        var config = new SoundConfig { DefaultPack = "assistant" };
        var assignment = new PackAssignment(TwoPacks, config);

        assignment.Resolve("nonexistent", "some-project").Should().Be("assistant");
    }

    [Fact]
    public void ProjectMapping_NonExistentPack_FallsThrough()
    {
        var config = new SoundConfig
        {
            DefaultPack = "retro",
            Projects = new() { ["my-project"] = "deleted-pack" }
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
        var config = new SoundConfig { DefaultPack = "" };
        var assignment = new PackAssignment(TwoPacks, config);
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
        // JSON deserialization can produce null Projects when config has "projects": null
        var config = new SoundConfig { DefaultPack = "assistant", Projects = null! };
        var assignment = new PackAssignment(TwoPacks, config);

        assignment.Resolve(null, "some-project").Should().Be("assistant");
    }

    [Fact]
    public void NullProjectMappings_WithProject_FallsToDefault()
    {
        var config = new SoundConfig { DefaultPack = "retro", Projects = null! };
        var assignment = new PackAssignment(TwoPacks, config);

        assignment.Resolve(null, "my-project").Should().Be("retro");
    }
}
