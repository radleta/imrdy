using FluentAssertions;
using Imrdy.Core.State;

namespace Imrdy.Core.Tests;

public class StateFileModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly StateFileReader _reader;

    public StateFileModelTests()
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

    private StateFileModel CreateBaseModel(string sessionId = "test-session") => new()
    {
        SessionId = sessionId,
        Status = "busy",
        Project = "test-project",
        Cwd = @"D:\dev\test",
        HookEvent = "UserPromptSubmit",
        Timestamp = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void IconStyle_RoundTrip_WithNonNullValue()
    {
        var path = Path.Combine(_tempDir, "icon-style.json");
        var model = CreateBaseModel() with { IconStyle = "circles" };

        _reader.WriteStateFile(path, model);
        var result = _reader.ReadStateFile(path);

        result.Should().NotBeNull();
        result!.IconStyle.Should().Be("circles");
    }

    [Fact]
    public void IconStyle_RoundTrip_WithNullValue()
    {
        var path = Path.Combine(_tempDir, "icon-style-null.json");
        var model = CreateBaseModel() with { IconStyle = null };

        _reader.WriteStateFile(path, model);
        var result = _reader.ReadStateFile(path);

        result.Should().NotBeNull();
        result!.IconStyle.Should().BeNull();
    }

    [Fact]
    public void IconStyle_JsonFieldName_IsIconStyle()
    {
        var path = Path.Combine(_tempDir, "icon-style-key.json");
        var model = CreateBaseModel() with { IconStyle = "squares" };

        _reader.WriteStateFile(path, model);
        var json = File.ReadAllText(path);

        json.Should().Contain("\"icon_style\"");
        json.Should().Contain("\"squares\"");
    }

    [Fact]
    public void LastTeammateAt_RoundTrip_WithValue()
    {
        var path = Path.Combine(_tempDir, "teammate-ts.json");
        var ts = new DateTimeOffset(2026, 4, 14, 12, 0, 0, TimeSpan.Zero);
        var model = CreateBaseModel() with { LastTeammateAt = ts };

        _reader.WriteStateFile(path, model);
        var result = _reader.ReadStateFile(path);

        result.Should().NotBeNull();
        result!.LastTeammateAt.Should().Be(ts);
    }

    [Fact]
    public void LastTeammateAt_RoundTrip_NullWhenAbsent()
    {
        var path = Path.Combine(_tempDir, "no-teammate-ts.json");
        var model = CreateBaseModel();

        _reader.WriteStateFile(path, model);
        var result = _reader.ReadStateFile(path);

        result.Should().NotBeNull();
        result!.LastTeammateAt.Should().BeNull();
    }

    [Fact]
    public void LastTeammateAt_JsonFieldName()
    {
        var path = Path.Combine(_tempDir, "teammate-key.json");
        var model = CreateBaseModel() with { LastTeammateAt = DateTimeOffset.UtcNow };

        _reader.WriteStateFile(path, model);
        var json = File.ReadAllText(path);

        json.Should().Contain("\"last_teammate_at\"");
    }

    [Fact]
    public void IconStyle_DefaultsToNull_WhenAbsentFromJson()
    {
        var path = Path.Combine(_tempDir, "no-icon-style.json");
        var model = CreateBaseModel();

        _reader.WriteStateFile(path, model);
        var result = _reader.ReadStateFile(path);

        result.Should().NotBeNull();
        result!.IconStyle.Should().BeNull();
    }
}
