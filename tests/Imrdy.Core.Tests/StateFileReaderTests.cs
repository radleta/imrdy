using Imrdy.Core.State;
using FluentAssertions;

namespace Imrdy.Core.Tests;

public class StateFileReaderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly StateFileReader _reader;

    public StateFileReaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "imrdy-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        _reader = new StateFileReader();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private StateFileModel CreateTestModel(string sessionId = "test-session") => new()
    {
        SessionId = sessionId,
        Status = "busy",
        Project = "test-project",
        Cwd = @"D:\dev\test",
        HookEvent = "UserPromptSubmit",
        Timestamp = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void ReadStateFile_ValidJson_ReturnsModel()
    {
        var path = Path.Combine(_tempDir, "valid.json");
        _reader.WriteStateFile(path, CreateTestModel());

        var result = _reader.ReadStateFile(path);

        result.Should().NotBeNull();
        result!.SessionId.Should().Be("test-session");
        result.Status.Should().Be("busy");
        result.Project.Should().Be("test-project");
    }

    [Fact]
    public void ReadStateFile_NonExistentFile_ReturnsNull()
    {
        var path = Path.Combine(_tempDir, "nonexistent.json");
        _reader.ReadStateFile(path).Should().BeNull();
    }

    [Fact]
    public void ReadStateFile_CorruptJson_ReturnsNull()
    {
        var path = Path.Combine(_tempDir, "corrupt.json");
        File.WriteAllText(path, "{ this is not json }");

        _reader.ReadStateFile(path).Should().BeNull();
    }

    [Fact]
    public void ReadStateFile_EmptyFile_ReturnsNull()
    {
        var path = Path.Combine(_tempDir, "empty.json");
        File.WriteAllText(path, "");

        _reader.ReadStateFile(path).Should().BeNull();
    }

    [Fact]
    public void ReadStateFile_WithBom_StripsAndReads()
    {
        var path = Path.Combine(_tempDir, "bom.json");
        var model = CreateTestModel("bom-session");
        // Write the model normally, then prepend BOM bytes
        _reader.WriteStateFile(path, model);
        var json = File.ReadAllBytes(path);
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var withBom = new byte[bom.Length + json.Length];
        bom.CopyTo(withBom, 0);
        json.CopyTo(withBom, bom.Length);
        File.WriteAllBytes(path, withBom);

        var result = _reader.ReadStateFile(path);

        result.Should().NotBeNull();
        result!.SessionId.Should().Be("bom-session");
    }

    [Fact]
    public void ReadStateFile_NullableFields_DefaultCorrectly()
    {
        var path = Path.Combine(_tempDir, "nulls.json");
        var model = CreateTestModel();

        _reader.WriteStateFile(path, model);
        var result = _reader.ReadStateFile(path);

        result.Should().NotBeNull();
        result!.ClaudePid.Should().BeNull();
        result.SoundPack.Should().BeNull();
        result.DesktopIndex.Should().BeNull();
        result.SessionName.Should().BeNull();
    }

    [Fact]
    public void WriteStateFile_AtomicWrite_CreatesFile()
    {
        var path = Path.Combine(_tempDir, "output.json");
        var model = CreateTestModel();

        _reader.WriteStateFile(path, model);

        File.Exists(path).Should().BeTrue();
        File.Exists(path + ".tmp").Should().BeFalse();
    }

    [Fact]
    public void WriteStateFile_CreatesDirectoryIfMissing()
    {
        var nestedDir = Path.Combine(_tempDir, "nested", "dir");
        var path = Path.Combine(nestedDir, "state.json");
        var model = CreateTestModel();

        _reader.WriteStateFile(path, model);

        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public void ReadAllStateFiles_ReturnsAllValid()
    {
        _reader.WriteStateFile(Path.Combine(_tempDir, "session1.json"), CreateTestModel("s1"));
        _reader.WriteStateFile(Path.Combine(_tempDir, "session2.json"), CreateTestModel("s2"));
        File.WriteAllText(Path.Combine(_tempDir, "corrupt.json"), "not json");

        var results = _reader.ReadAllStateFiles(_tempDir);

        results.Should().HaveCount(2);
        results.Select(r => r.SessionId).Should().BeEquivalentTo(["s1", "s2"]);
    }

    [Fact]
    public void ReadAllStateFiles_EmptyDirectory_ReturnsEmpty()
    {
        var emptyDir = Path.Combine(_tempDir, "empty");
        Directory.CreateDirectory(emptyDir);

        _reader.ReadAllStateFiles(emptyDir).Should().BeEmpty();
    }

    [Fact]
    public void ReadAllStateFiles_NonExistentDirectory_ReturnsEmpty()
    {
        _reader.ReadAllStateFiles(Path.Combine(_tempDir, "nope")).Should().BeEmpty();
    }

    [Fact]
    public void RemoveStateFile_RemovesBothFiles()
    {
        var statePath = Path.Combine(_tempDir, "abc123.json");
        var pidPath = Path.Combine(_tempDir, ".pid-abc123");
        File.WriteAllText(statePath, "{}");
        File.WriteAllText(pidPath, "12345");

        _reader.RemoveStateFile(_tempDir, "abc123");

        File.Exists(statePath).Should().BeFalse();
        File.Exists(pidPath).Should().BeFalse();
    }

    [Fact]
    public void RemoveStateFile_MissingFiles_DoesNotThrow()
    {
        var action = () => _reader.RemoveStateFile(_tempDir, "nonexistent");
        action.Should().NotThrow();
    }

    [Fact]
    public void WriteStateFile_NoBomInOutput()
    {
        var path = Path.Combine(_tempDir, "no-bom.json");
        _reader.WriteStateFile(path, CreateTestModel());

        var bytes = File.ReadAllBytes(path);
        // Verify no BOM: first byte should be '{' (0x7B), not BOM marker (0xEF)
        bytes[0].Should().NotBe(0xEF);
    }

    [Fact]
    public void ReadWriteRoundTrip_AllFieldsPreserved()
    {
        var path = Path.Combine(_tempDir, "roundtrip.json");
        var model = new StateFileModel
        {
            SessionId = "rt-session",
            Status = "permission",
            Project = "my-project",
            Cwd = @"D:\dev\my-project",
            HookEvent = "PermissionRequest",
            NotificationType = "permission_prompt",
            LastMessage = "Can I write to file.txt?",
            ClaudePid = 42,
            SoundPack = "assistant",
            DesktopIndex = 2,
            Timestamp = DateTimeOffset.Parse("2025-03-31T12:00:00Z"),
            SessionName = "test-name",
        };

        _reader.WriteStateFile(path, model);
        var result = _reader.ReadStateFile(path);

        result.Should().NotBeNull();
        result!.SessionId.Should().Be("rt-session");
        result.Status.Should().Be("permission");
        result.Project.Should().Be("my-project");
        result.Cwd.Should().Be(@"D:\dev\my-project");
        result.HookEvent.Should().Be("PermissionRequest");
        result.NotificationType.Should().Be("permission_prompt");
        result.LastMessage.Should().Be("Can I write to file.txt?");
        result.ClaudePid.Should().Be(42);
        result.SoundPack.Should().Be("assistant");
        result.DesktopIndex.Should().Be(2);
        result.SessionName.Should().Be("test-name");
    }
}
