using System.Text.Json;
using Imrdy.Core.Validation;
using FluentAssertions;

namespace Imrdy.Core.Tests;

public class PackValidatorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly PackValidator _validator = new();

    public PackValidatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "imrdy-packval-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private string CreateValidPack(string name = "test-pack")
    {
        var packDir = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(packDir);

        var packJson = new
        {
            name,
            description = $"A test pack",
            version = "1.0.0",
            events = new Dictionary<string, object>
            {
                ["session_start"] = new { folder = "session_start" },
                ["finished"] = new { folder = "finished" },
            }
        };

        File.WriteAllText(
            Path.Combine(packDir, "pack.json"),
            JsonSerializer.Serialize(packJson));

        // Create event folders with WAV files
        var startDir = Path.Combine(packDir, "session_start");
        Directory.CreateDirectory(startDir);
        File.WriteAllBytes(Path.Combine(startDir, "start.wav"), [0xFF, 0xFE, 0x01]);

        var finishedDir = Path.Combine(packDir, "finished");
        Directory.CreateDirectory(finishedDir);
        File.WriteAllBytes(Path.Combine(finishedDir, "done.wav"), [0xFF, 0xFE, 0x01]);

        return packDir;
    }

    [Fact]
    public void Validate_ValidPack_IsValid()
    {
        var packDir = CreateValidPack();
        var result = _validator.Validate(packDir);

        result.IsValid.Should().BeTrue();
        result.Errors.Where(e => e.Severity == ValidationSeverity.Error).Should().BeEmpty();
    }

    [Fact]
    public void Validate_MissingPackJson_Error()
    {
        var packDir = Path.Combine(_tempDir, "no-pack");
        Directory.CreateDirectory(packDir);

        var result = _validator.Validate(packDir);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("pack.json not found"));
    }

    [Fact]
    public void Validate_CorruptJson_Error()
    {
        var packDir = Path.Combine(_tempDir, "corrupt");
        Directory.CreateDirectory(packDir);
        File.WriteAllText(Path.Combine(packDir, "pack.json"), "not json");

        var result = _validator.Validate(packDir);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("Invalid JSON"));
    }

    [Fact]
    public void Validate_MissingName_Error()
    {
        var packDir = Path.Combine(_tempDir, "no-name");
        Directory.CreateDirectory(packDir);
        File.WriteAllText(Path.Combine(packDir, "pack.json"),
            """{"description": "test", "version": "1.0", "events": {}}""");

        var result = _validator.Validate(packDir);

        result.Errors.Should().Contain(e => e.Message.Contains("name"));
    }

    [Fact]
    public void Validate_MissingDescription_Error()
    {
        var packDir = Path.Combine(_tempDir, "no-desc");
        Directory.CreateDirectory(packDir);
        File.WriteAllText(Path.Combine(packDir, "pack.json"),
            """{"name": "test", "version": "1.0", "events": {}}""");

        var result = _validator.Validate(packDir);

        result.Errors.Should().Contain(e => e.Message.Contains("description"));
    }

    [Fact]
    public void Validate_MissingVersion_Error()
    {
        var packDir = Path.Combine(_tempDir, "no-ver");
        Directory.CreateDirectory(packDir);
        File.WriteAllText(Path.Combine(packDir, "pack.json"),
            """{"name": "test", "description": "test", "events": {}}""");

        var result = _validator.Validate(packDir);

        result.Errors.Should().Contain(e => e.Message.Contains("version"));
    }

    [Fact]
    public void Validate_NoEvents_Warning()
    {
        var packDir = Path.Combine(_tempDir, "no-events");
        Directory.CreateDirectory(packDir);
        File.WriteAllText(Path.Combine(packDir, "pack.json"),
            """{"name": "test", "description": "test", "version": "1.0", "events": {}}""");

        var result = _validator.Validate(packDir);

        result.IsValid.Should().BeTrue(); // warnings don't make it invalid
        result.Errors.Should().Contain(e =>
            e.Message.Contains("No events") && e.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public void Validate_UnknownEventName_Warning()
    {
        var packDir = Path.Combine(_tempDir, "unknown-event");
        Directory.CreateDirectory(packDir);
        File.WriteAllText(Path.Combine(packDir, "pack.json"),
            """{"name": "t", "description": "t", "version": "1.0", "events": {"bogus_event": {"folder": "bogus"}}}""");

        var result = _validator.Validate(packDir);

        result.Errors.Should().Contain(e =>
            e.Message.Contains("Unknown event") && e.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public void Validate_MissingEventFolder_Error()
    {
        var packDir = Path.Combine(_tempDir, "missing-folder");
        Directory.CreateDirectory(packDir);
        File.WriteAllText(Path.Combine(packDir, "pack.json"),
            """{"name": "t", "description": "t", "version": "1.0", "events": {"session_start": {"folder": "nonexistent"}}}""");

        var result = _validator.Validate(packDir);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Message.Contains("does not exist"));
    }

    [Fact]
    public void Validate_EmptyEventFolder_Error()
    {
        var packDir = Path.Combine(_tempDir, "empty-folder");
        Directory.CreateDirectory(packDir);
        File.WriteAllText(Path.Combine(packDir, "pack.json"),
            """{"name": "t", "description": "t", "version": "1.0", "events": {"session_start": {"folder": "session_start"}}}""");
        Directory.CreateDirectory(Path.Combine(packDir, "session_start"));

        var result = _validator.Validate(packDir);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Message.Contains("No .wav files"));
    }

    [Fact]
    public void Validate_EmptyWavFile_Error()
    {
        var packDir = Path.Combine(_tempDir, "empty-wav");
        Directory.CreateDirectory(packDir);
        File.WriteAllText(Path.Combine(packDir, "pack.json"),
            """{"name": "t", "description": "t", "version": "1.0", "events": {"session_start": {"folder": "session_start"}}}""");
        var eventDir = Path.Combine(packDir, "session_start");
        Directory.CreateDirectory(eventDir);
        File.WriteAllBytes(Path.Combine(eventDir, "empty.wav"), []);

        var result = _validator.Validate(packDir);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Message.Contains("empty (zero bytes)"));
    }

    [Fact]
    public void Validate_MissingFolderProperty_Error()
    {
        var packDir = Path.Combine(_tempDir, "no-folder-prop");
        Directory.CreateDirectory(packDir);
        File.WriteAllText(Path.Combine(packDir, "pack.json"),
            """{"name": "t", "description": "t", "version": "1.0", "events": {"session_start": {"folder": ""}}}""");

        var result = _validator.Validate(packDir);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Message.Contains("folder"));
    }
}
