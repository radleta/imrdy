using System.Text.Json;
using Imrdy.Core.Sound;
using FluentAssertions;

namespace Imrdy.Core.Tests;

public class PackLoaderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly PackLoader _loader;

    public PackLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "imrdy-pack-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _loader = new PackLoader();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private string CreatePackDir(string name, Dictionary<string, string[]>? events = null)
    {
        var packDir = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(packDir);

        var eventsDict = events?.ToDictionary(
            e => e.Key,
            e => (object)new { folder = e.Key }) ?? new Dictionary<string, object>();

        var packJson = new
        {
            name,
            description = $"Test pack {name}",
            version = "1.0.0",
            events = eventsDict
        };

        File.WriteAllText(
            Path.Combine(packDir, "pack.json"),
            JsonSerializer.Serialize(packJson));

        if (events is not null)
        {
            foreach (var (eventName, wavFiles) in events)
            {
                var eventDir = Path.Combine(packDir, eventName);
                Directory.CreateDirectory(eventDir);
                foreach (var wav in wavFiles)
                {
                    File.WriteAllBytes(Path.Combine(eventDir, wav), [0xFF, 0xFF]);
                }
            }
        }

        return packDir;
    }

    [Fact]
    public void LoadPacks_DiscoversPacks()
    {
        CreatePackDir("assistant", new()
        {
            ["session_start"] = ["start1.wav", "start2.wav"],
            ["finished"] = ["done.wav"],
        });
        CreatePackDir("retro", new()
        {
            ["getting_to_work"] = ["work.wav"],
        });

        var packs = _loader.LoadPacks(_tempDir);

        packs.Should().HaveCount(2);
        packs.Select(p => p.Name).Should().BeEquivalentTo(["assistant", "retro"]);
    }

    [Fact]
    public void LoadPacks_CountsWavFiles()
    {
        CreatePackDir("assistant", new()
        {
            ["session_start"] = ["s1.wav", "s2.wav", "s3.wav"],
            ["finished"] = ["done.wav"],
        });

        var packs = _loader.LoadPacks(_tempDir);
        var pack = packs.Single();

        pack.WavFiles[SoundEvent.SessionStart].Should().HaveCount(3);
        pack.WavFiles[SoundEvent.Finished].Should().HaveCount(1);
    }

    [Fact]
    public void LoadPacks_MissingPackJson_SkipsPack()
    {
        var emptyDir = Path.Combine(_tempDir, "empty-pack");
        Directory.CreateDirectory(emptyDir);

        var packs = _loader.LoadPacks(_tempDir);
        packs.Should().BeEmpty();
    }

    [Fact]
    public void LoadPacks_CorruptPackJson_SkipsPack()
    {
        var packDir = Path.Combine(_tempDir, "broken");
        Directory.CreateDirectory(packDir);
        File.WriteAllText(Path.Combine(packDir, "pack.json"), "not json");

        var packs = _loader.LoadPacks(_tempDir);
        packs.Should().BeEmpty();
    }

    [Fact]
    public void LoadPacks_EmptyEvents_LoadsWithNoWavs()
    {
        CreatePackDir("minimal");

        var packs = _loader.LoadPacks(_tempDir);
        packs.Should().HaveCount(1);
        packs[0].WavFiles.Should().BeEmpty();
    }

    [Fact]
    public void LoadPacks_NonExistentDirectory_ReturnsEmpty()
    {
        _loader.LoadPacks(Path.Combine(_tempDir, "nope")).Should().BeEmpty();
    }

    [Fact]
    public void LoadPacks_UnknownEventName_Skipped()
    {
        CreatePackDir("weird", new()
        {
            ["nonexistent_event"] = ["sound.wav"],
            ["session_start"] = ["start.wav"],
        });

        var packs = _loader.LoadPacks(_tempDir);
        var pack = packs.Single();

        pack.WavFiles.Should().HaveCount(1);
        pack.WavFiles.Should().ContainKey(SoundEvent.SessionStart);
    }

    [Fact]
    public void LoadPack_MissingName_ReturnsNull()
    {
        var packDir = Path.Combine(_tempDir, "no-name");
        Directory.CreateDirectory(packDir);
        File.WriteAllText(Path.Combine(packDir, "pack.json"),
            """{"description": "no name", "version": "1.0", "events": {}}""");

        _loader.LoadPack(packDir, Path.Combine(packDir, "pack.json")).Should().BeNull();
    }
}
