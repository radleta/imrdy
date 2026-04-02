using Imrdy.Core.Validation;
using FluentAssertions;

namespace Imrdy.Core.Tests;

public class ConfigValidatorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ConfigValidator _validator = new();
    private static readonly string[] AvailablePacks = ["assistant", "retro"];

    public ConfigValidatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "imrdy-cfgval-tests", Guid.NewGuid().ToString());
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
    public void Validate_ValidConfig_IsValid()
    {
        var path = Path.Combine(_tempDir, "config.json");
        File.WriteAllText(path,
            """{"default": "assistant", "projectMappings": {"my-proj": "retro"}}""");

        var result = _validator.Validate(path, AvailablePacks);

        result.IsValid.Should().BeTrue();
        result.Errors.Where(e => e.Severity == ValidationSeverity.Error).Should().BeEmpty();
    }

    [Fact]
    public void Validate_MissingFile_Warning()
    {
        var path = Path.Combine(_tempDir, "nope.json");

        var result = _validator.Validate(path, AvailablePacks);

        result.IsValid.Should().BeTrue(); // warning only
        result.Errors.Should().ContainSingle(e =>
            e.Severity == ValidationSeverity.Warning && e.Message.Contains("not found"));
    }

    [Fact]
    public void Validate_CorruptJson_Error()
    {
        var path = Path.Combine(_tempDir, "bad.json");
        File.WriteAllText(path, "not json");

        var result = _validator.Validate(path, AvailablePacks);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("Invalid JSON"));
    }

    [Fact]
    public void Validate_UnknownKey_Warning()
    {
        var path = Path.Combine(_tempDir, "config.json");
        File.WriteAllText(path,
            """{"default": "assistant", "unknownKey": "value"}""");

        var result = _validator.Validate(path, AvailablePacks);

        result.IsValid.Should().BeTrue(); // warnings don't invalidate
        result.Errors.Should().Contain(e =>
            e.Severity == ValidationSeverity.Warning && e.Message.Contains("Unknown key"));
    }

    [Fact]
    public void Validate_DanglingDefaultPackReference_Error()
    {
        var path = Path.Combine(_tempDir, "config.json");
        File.WriteAllText(path, """{"default": "deleted-pack"}""");

        var result = _validator.Validate(path, AvailablePacks);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.Message.Contains("deleted-pack") && e.Message.Contains("not installed"));
    }

    [Fact]
    public void Validate_DanglingProjectMappingPackReference_Error()
    {
        var path = Path.Combine(_tempDir, "config.json");
        File.WriteAllText(path,
            """{"projectMappings": {"my-proj": "nonexistent-pack"}}""");

        var result = _validator.Validate(path, AvailablePacks);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.Message.Contains("nonexistent-pack") && e.Message.Contains("my-proj"));
    }

    [Fact]
    public void Validate_CaseInsensitivePackLookup()
    {
        var path = Path.Combine(_tempDir, "config.json");
        File.WriteAllText(path, """{"default": "ASSISTANT"}""");

        var result = _validator.Validate(path, AvailablePacks);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyConfig_IsValid()
    {
        var path = Path.Combine(_tempDir, "config.json");
        File.WriteAllText(path, "{}");

        var result = _validator.Validate(path, AvailablePacks);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_RootNotObject_Error()
    {
        var path = Path.Combine(_tempDir, "config.json");
        File.WriteAllText(path, "[]");

        var result = _validator.Validate(path, AvailablePacks);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Message.Contains("JSON object"));
    }
}
