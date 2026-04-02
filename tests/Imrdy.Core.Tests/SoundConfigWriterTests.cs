using System.Text.Json;
using Imrdy.Core.Sound;
using FluentAssertions;

namespace Imrdy.Core.Tests;

public class SoundConfigWriterTests : IDisposable
{
    private readonly string _tempDir;

    public SoundConfigWriterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "imrdy-writer-tests", Guid.NewGuid().ToString());
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
    public void Save_CreatesValidJsonFile()
    {
        var path = Path.Combine(_tempDir, "config.json");
        var config = new SoundConfig { Default = "assistant" };

        SoundConfigWriter.Save(config, path);

        File.Exists(path).Should().BeTrue();
        var bytes = File.ReadAllBytes(path);
        var doc = JsonDocument.Parse(bytes);
        doc.RootElement.GetProperty("default").GetString().Should().Be("assistant");
    }

    [Fact]
    public void Save_CreatesParentDirectoryIfMissing()
    {
        var nestedDir = Path.Combine(_tempDir, "nested", "deep");
        var path = Path.Combine(nestedDir, "config.json");
        var config = new SoundConfig();

        SoundConfigWriter.Save(config, path);

        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public void Save_OverwritesExistingFile()
    {
        var path = Path.Combine(_tempDir, "config.json");
        SoundConfigWriter.Save(new SoundConfig { Default = "old" }, path);
        SoundConfigWriter.Save(new SoundConfig { Default = "new" }, path);

        var bytes = File.ReadAllBytes(path);
        var result = JsonSerializer.Deserialize(bytes, ImrdyJsonContext.Default.SoundConfig);
        result!.Default.Should().Be("new");
    }

    [Fact]
    public void Save_NoTmpFileLeftBehind()
    {
        var path = Path.Combine(_tempDir, "config.json");
        SoundConfigWriter.Save(new SoundConfig(), path);

        File.Exists(path + ".tmp").Should().BeFalse();
    }

    [Fact]
    public void DefaultSoundConfig_HasSoundEnabledTrue()
    {
        var config = new SoundConfig();

        config.SoundEnabled.Should().BeTrue();
    }

    [Fact]
    public void SoundEnabled_False_RoundTrips()
    {
        var path = Path.Combine(_tempDir, "config.json");
        var config = new SoundConfig { SoundEnabled = false };

        SoundConfigWriter.Save(config, path);

        var bytes = File.ReadAllBytes(path);
        var result = JsonSerializer.Deserialize(bytes, ImrdyJsonContext.Default.SoundConfig);
        result!.SoundEnabled.Should().BeFalse();
    }

    [Fact]
    public void SoundEnabled_True_RoundTrips()
    {
        var path = Path.Combine(_tempDir, "config.json");
        var config = new SoundConfig { SoundEnabled = true };

        SoundConfigWriter.Save(config, path);

        var bytes = File.ReadAllBytes(path);
        var result = JsonSerializer.Deserialize(bytes, ImrdyJsonContext.Default.SoundConfig);
        result!.SoundEnabled.Should().BeTrue();
    }

    [Fact]
    public void Save_PreservesAllFields()
    {
        var path = Path.Combine(_tempDir, "config.json");
        var config = new SoundConfig
        {
            Default = "retro",
            ProjectMappings = new Dictionary<string, string> { ["proj"] = "assistant" },
            SoundEnabled = false,
        };

        SoundConfigWriter.Save(config, path);

        var bytes = File.ReadAllBytes(path);
        var result = JsonSerializer.Deserialize(bytes, ImrdyJsonContext.Default.SoundConfig);
        result!.Default.Should().Be("retro");
        result.ProjectMappings.Should().ContainKey("proj").WhoseValue.Should().Be("assistant");
        result.SoundEnabled.Should().BeFalse();
    }
}
