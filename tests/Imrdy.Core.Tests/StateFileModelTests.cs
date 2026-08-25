using FluentAssertions;
using Imrdy.Core.Hooks;
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
    public void IconStyle_DefaultsToNull_WhenAbsentFromJson()
    {
        var path = Path.Combine(_tempDir, "no-icon-style.json");
        var model = CreateBaseModel();

        _reader.WriteStateFile(path, model);
        var result = _reader.ReadStateFile(path);

        result.Should().NotBeNull();
        result!.IconStyle.Should().BeNull();
    }

    [Fact]
    public void RunningTasks_RoundTrip_WithEntries()
    {
        // Payloads drawn verbatim from scratch/agent-liveness-roster/evidence/capture.log
        // (2026-08-20 13:20:38.976 SubagentStop entry). `command` is intentionally
        // unmodelled on BackgroundTaskModel per spec §4.1.
        var path = Path.Combine(_tempDir, "running-tasks.json");
        var tasks = new List<BackgroundTaskModel>
        {
            new()
            {
                Id = "bk44y8t1j",
                Type = "shell",
                Status = "running",
                Description = "find / -ipath \"*Microsoft.AspNetCore.Antiforgery*\" -iname \"*.dll\" 2>/dev/null | head -20",
            },
            new()
            {
                Id = "a10105756c8021221",
                Type = "subagent",
                Status = "running",
                Description = "Extend antiforgery fix in spec.md",
                AgentType = "general-purpose",
            },
        };
        var model = CreateBaseModel() with { RunningTasks = tasks };

        _reader.WriteStateFile(path, model);
        var result = _reader.ReadStateFile(path);

        result.Should().NotBeNull();
        result!.RunningTasks.Should().HaveCount(2);
        result.RunningTasks![0].Id.Should().Be("bk44y8t1j");
        result.RunningTasks[0].Type.Should().Be("shell");
        result.RunningTasks[0].Status.Should().Be("running");
        result.RunningTasks[0].Description.Should().Be("find / -ipath \"*Microsoft.AspNetCore.Antiforgery*\" -iname \"*.dll\" 2>/dev/null | head -20");
        result.RunningTasks[0].AgentType.Should().BeNull();
        result.RunningTasks[1].Id.Should().Be("a10105756c8021221");
        result.RunningTasks[1].Type.Should().Be("subagent");
        result.RunningTasks[1].Status.Should().Be("running");
        result.RunningTasks[1].Description.Should().Be("Extend antiforgery fix in spec.md");
        result.RunningTasks[1].AgentType.Should().Be("general-purpose");
    }

    [Fact]
    public void RunningTasks_RoundTrip_NullWhenAbsent()
    {
        var path = Path.Combine(_tempDir, "no-running-tasks.json");
        var model = CreateBaseModel();

        _reader.WriteStateFile(path, model);
        var result = _reader.ReadStateFile(path);

        result.Should().NotBeNull();
        result!.RunningTasks.Should().BeNull();
    }

    [Fact]
    public void RunningTasks_RoundTrip_EmptyListSurvivesAsEmpty()
    {
        var path = Path.Combine(_tempDir, "empty-running-tasks.json");
        var model = CreateBaseModel() with { RunningTasks = [] };

        _reader.WriteStateFile(path, model);
        var result = _reader.ReadStateFile(path);

        result.Should().NotBeNull();
        result!.RunningTasks.Should().NotBeNull();
        result.RunningTasks.Should().BeEmpty();
    }

    [Fact]
    public void RunningTasks_JsonFieldName()
    {
        var path = Path.Combine(_tempDir, "running-tasks-key.json");
        var model = CreateBaseModel() with
        {
            RunningTasks = [new BackgroundTaskModel { Id = "bk44y8t1j", Type = "shell", Status = "running" }],
        };

        _reader.WriteStateFile(path, model);
        var json = File.ReadAllText(path);

        json.Should().Contain("\"running_tasks\"");
    }
}
