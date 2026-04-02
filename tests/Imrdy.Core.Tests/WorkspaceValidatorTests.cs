using Imrdy.Core.Validation;
using FluentAssertions;

namespace Imrdy.Core.Tests;

public class WorkspaceValidatorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly WorkspaceValidator _validator = new();

    public WorkspaceValidatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "imrdy-wsval-tests", Guid.NewGuid().ToString());
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
        var path = Path.Combine(_tempDir, "workspaces.json");
        // Use the temp dir itself as a real existing path
        File.WriteAllText(path,
            $$"""{"workspaces": [{"path": "{{_tempDir.Replace("\\", "\\\\")}}", "name": "test", "desktop": 1}]}""");

        var result = _validator.Validate(path);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_MissingFile_Warning()
    {
        var result = _validator.Validate(Path.Combine(_tempDir, "nope.json"));

        result.IsValid.Should().BeTrue();
        result.Errors.Should().ContainSingle(e =>
            e.Severity == ValidationSeverity.Warning && e.Message.Contains("not found"));
    }

    [Fact]
    public void Validate_CorruptJson_Error()
    {
        var path = Path.Combine(_tempDir, "bad.json");
        File.WriteAllText(path, "not json");

        var result = _validator.Validate(path);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_MissingPath_Error()
    {
        var path = Path.Combine(_tempDir, "workspaces.json");
        File.WriteAllText(path,
            """{"workspaces": [{"name": "test", "desktop": 1}]}""");

        var result = _validator.Validate(path);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Message.Contains("path"));
    }

    [Fact]
    public void Validate_MissingName_Error()
    {
        var path = Path.Combine(_tempDir, "workspaces.json");
        File.WriteAllText(path,
            $$"""{"workspaces": [{"path": "{{_tempDir.Replace("\\", "\\\\")}}", "desktop": 1}]}""");

        var result = _validator.Validate(path);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Message.Contains("name"));
    }

    [Fact]
    public void Validate_MissingDesktop_Error()
    {
        var path = Path.Combine(_tempDir, "workspaces.json");
        File.WriteAllText(path,
            $$"""{"workspaces": [{"path": "{{_tempDir.Replace("\\", "\\\\")}}", "name": "test"}]}""");

        var result = _validator.Validate(path);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Message.Contains("desktop"));
    }

    [Fact]
    public void Validate_NonExistentPath_Warning()
    {
        var path = Path.Combine(_tempDir, "workspaces.json");
        File.WriteAllText(path,
            """{"workspaces": [{"path": "Z:\\nonexistent\\path", "name": "test", "desktop": 1}]}""");

        var result = _validator.Validate(path);

        result.IsValid.Should().BeTrue(); // warning, not error
        result.Errors.Should().Contain(e =>
            e.Severity == ValidationSeverity.Warning && e.Message.Contains("does not exist"));
    }

    [Fact]
    public void Validate_MissingWorkspacesArray_Error()
    {
        var path = Path.Combine(_tempDir, "workspaces.json");
        File.WriteAllText(path, """{"other": "stuff"}""");

        var result = _validator.Validate(path);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Message.Contains("workspaces"));
    }

    [Fact]
    public void Validate_EmptyWorkspacesArray_IsValid()
    {
        var path = Path.Combine(_tempDir, "workspaces.json");
        File.WriteAllText(path, """{"workspaces": []}""");

        var result = _validator.Validate(path);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_RootNotObject_Error()
    {
        var path = Path.Combine(_tempDir, "workspaces.json");
        File.WriteAllText(path, "[]");

        var result = _validator.Validate(path);

        result.IsValid.Should().BeFalse();
    }
}
