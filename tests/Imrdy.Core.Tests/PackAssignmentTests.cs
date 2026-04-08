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
    public void Priority3_RandomDefault_ReturnsEnabledPack()
    {
        var config = new SoundConfig { DefaultPack = "random" };
        var assignment = new PackAssignment(TwoPacks, config);

        var result = assignment.Resolve(null, null);
        result.Should().NotBeNull();
        result.Should().BeOneOf("assistant", "retro");
    }

    [Fact]
    public void Priority3_RandomDefault_ExcludesDisabledPacks()
    {
        var config = new SoundConfig
        {
            DefaultPack = "random",
            DisabledPacks = ["assistant"]
        };
        var assignment = new PackAssignment(TwoPacks, config);

        // With assistant disabled, random must always pick retro
        for (var i = 0; i < 20; i++)
        {
            assignment.Resolve(null, null).Should().Be("retro");
        }
    }

    [Fact]
    public void Priority3_RandomDefault_AllDisabled_ReturnsNull()
    {
        var config = new SoundConfig
        {
            DefaultPack = "random",
            DisabledPacks = ["assistant", "retro"]
        };
        var assignment = new PackAssignment(TwoPacks, config);

        assignment.Resolve(null, null).Should().BeNull();
    }

    [Fact]
    public void Priority3_EmptyDefault_MeansNone()
    {
        var config = new SoundConfig { DefaultPack = "" };
        var assignment = new PackAssignment(TwoPacks, config);

        // Empty default means none — falls through to priority 4/5
        // With no CLI default and multiple packs, returns null
        assignment.Resolve(null, null).Should().BeNull();
    }

    [Fact]
    public void Priority4_CliDefault()
    {
        var config = new SoundConfig { DefaultPack = "" };
        var assignment = new PackAssignment(TwoPacks, config, cliDefault: "retro");

        assignment.Resolve(null, null).Should().Be("retro");
    }

    [Fact]
    public void Priority4_CliDefault_DisabledPack_FallsThrough()
    {
        var config = new SoundConfig
        {
            DefaultPack = "",
            DisabledPacks = ["retro"]
        };
        var assignment = new PackAssignment(TwoPacks, config, cliDefault: "retro");

        // CLI default is disabled, only one enabled pack left → auto-detect
        assignment.Resolve(null, null).Should().Be("assistant");
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
    public void StateFileOverride_DisabledPack_StillHonored()
    {
        var config = new SoundConfig
        {
            DefaultPack = "retro",
            DisabledPacks = ["assistant"]
        };
        var assignment = new PackAssignment(TwoPacks, config);

        // Explicit state file override is respected even if pack is disabled
        assignment.Resolve("assistant", null).Should().Be("assistant");
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
    public void ProjectMapping_DisabledPack_FallsThrough()
    {
        var config = new SoundConfig
        {
            DefaultPack = "retro",
            DisabledPacks = ["assistant"],
            Projects = new() { ["my-project"] = "assistant" }
        };
        var assignment = new PackAssignment(TwoPacks, config);

        assignment.Resolve(null, "my-project").Should().Be("retro");
    }

    [Fact]
    public void ConfigDefault_DisabledPack_FallsThrough()
    {
        var config = new SoundConfig
        {
            DefaultPack = "assistant",
            DisabledPacks = ["assistant"]
        };
        var assignment = new PackAssignment(TwoPacks, config, cliDefault: "retro");

        assignment.Resolve(null, null).Should().Be("retro");
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
    public void AutoDetect_SkipsDisabledPacks()
    {
        var packs = new[] { MakePack("enabled-one"), MakePack("disabled-one") };
        var config = new SoundConfig
        {
            DefaultPack = "",
            DisabledPacks = ["disabled-one"]
        };
        var assignment = new PackAssignment(packs, config);

        // Only one enabled pack → auto-detect picks it
        assignment.Resolve(null, null).Should().Be("enabled-one");
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

    [Fact]
    public void CaseInsensitive_DisabledPackMatching()
    {
        var config = new SoundConfig
        {
            DefaultPack = "random",
            DisabledPacks = ["ASSISTANT"]
        };
        var assignment = new PackAssignment(TwoPacks, config);

        // "ASSISTANT" in disabled list should match "assistant" pack (case-insensitive)
        for (var i = 0; i < 20; i++)
        {
            assignment.Resolve(null, null).Should().Be("retro");
        }
    }
}
