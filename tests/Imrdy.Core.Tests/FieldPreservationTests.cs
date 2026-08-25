using Imrdy.Core.Hooks;
using Imrdy.Core.State;
using FluentAssertions;

namespace Imrdy.Core.Tests;

public class FieldPreservationTests
{
    private static StateFileModel CreateModel(
        string? soundPack = null,
        int? desktopIndex = null,
        string lastMessage = "",
        string? iconStyle = null) => new()
    {
        SessionId = "test",
        Status = "busy",
        Project = "test",
        Cwd = @"D:\dev\test",
        HookEvent = "UserPromptSubmit",
        Timestamp = DateTimeOffset.UtcNow,
        SoundPack = soundPack,
        DesktopIndex = desktopIndex,
        LastMessage = lastMessage,
        IconStyle = iconStyle,
    };

    [Fact]
    public void PreserveFields_NullExisting_ReturnsNewState()
    {
        var newState = CreateModel(soundPack: "jazz");

        var result = FieldPreservation.PreserveFields(newState, null);

        result.SoundPack.Should().Be("jazz");
    }

    [Fact]
    public void PreserveFields_PreservesSoundPackFromExisting()
    {
        var existing = CreateModel(soundPack: "assistant");
        var newState = CreateModel(soundPack: null);

        var result = FieldPreservation.PreserveFields(newState, existing);

        result.SoundPack.Should().Be("assistant");
    }

    [Fact]
    public void PreserveFields_PreservesDesktopIndexFromExisting()
    {
        var existing = CreateModel(desktopIndex: 3);
        var newState = CreateModel(desktopIndex: null);

        var result = FieldPreservation.PreserveFields(newState, existing);

        result.DesktopIndex.Should().Be(3);
    }

    [Fact]
    public void PreserveFields_NewValueTakesPrecedence()
    {
        var existing = CreateModel(soundPack: "old-pack", desktopIndex: 1);
        var newState = CreateModel(soundPack: "new-pack", desktopIndex: 5);

        var result = FieldPreservation.PreserveFields(newState, existing);

        result.SoundPack.Should().Be("new-pack");
        result.DesktopIndex.Should().Be(5);
    }

    [Fact]
    public void PreserveFields_PreservesIconStyleFromExisting()
    {
        var existing = CreateModel(iconStyle: "triangles");
        var newState = CreateModel(iconStyle: null);

        var result = FieldPreservation.PreserveFields(newState, existing);

        result.IconStyle.Should().Be("triangles");
    }

    [Fact]
    public void PreserveFields_NewIconStyleTakesPrecedence()
    {
        var existing = CreateModel(iconStyle: "triangles");
        var newState = CreateModel(iconStyle: "squares");

        var result = FieldPreservation.PreserveFields(newState, existing);

        result.IconStyle.Should().Be("squares");
    }

    [Fact]
    public void PreserveFields_BothNull_StaysNull()
    {
        var existing = CreateModel();
        var newState = CreateModel();

        var result = FieldPreservation.PreserveFields(newState, existing);

        result.SoundPack.Should().BeNull();
        result.DesktopIndex.Should().BeNull();
        result.IconStyle.Should().BeNull();
    }

    [Fact]
    public void PreserveFields_WslDistro_PreservesFromExistingWhenNewIsNull()
    {
        var existing = CreateModel() with { WslDistro = "Ubuntu-22.04" };
        var newState = CreateModel();

        var result = FieldPreservation.PreserveFields(newState, existing);

        result.WslDistro.Should().Be("Ubuntu-22.04");
    }

    [Fact]
    public void PreserveFields_WslDistro_NewValueTakesPrecedence()
    {
        var existing = CreateModel() with { WslDistro = "Ubuntu-22.04" };
        var newState = CreateModel() with { WslDistro = "Ubuntu-24.04" };

        var result = FieldPreservation.PreserveFields(newState, existing);

        result.WslDistro.Should().Be("Ubuntu-24.04");
    }

    [Fact]
    public void PreserveFields_RunningTasks_PreservesFromExisting()
    {
        // Payload drawn verbatim from scratch/agent-liveness-roster/evidence/capture.log.
        var runningTasks = new List<BackgroundTaskModel>
        {
            new()
            {
                Id = "a10105756c8021221",
                Type = "subagent",
                Status = "running",
                Description = "Extend antiforgery fix in spec.md",
                AgentType = "general-purpose",
            },
        };
        var existing = CreateModel() with { RunningTasks = runningTasks };
        var newState = CreateModel();

        var result = FieldPreservation.PreserveFields(newState, existing);

        result.RunningTasks.Should().BeSameAs(runningTasks);
    }

    [Fact]
    public void PreserveFields_RunningTasks_NewValueTakesPrecedence()
    {
        // spec §8 E10 (parallel agents finishing one by one): the roster is never
        // mutated in place — each Stop delivers a whole replacement — so this
        // overwrite, repeated across successive Stops, IS the "decrements
        // monotonically" behavior E10 describes.
        var existingTasks = new List<BackgroundTaskModel>
        {
            new() { Id = "ab1a03f4c04d0844b", Type = "subagent", Status = "running", Description = "Timing probe 150s silent", AgentType = "general-purpose" },
            new() { Id = "a81d9ab9277c7fdbb", Type = "subagent", Status = "running", Description = "Iteration-8 plan fix pass", AgentType = "general-purpose" },
        };
        var newTasks = new List<BackgroundTaskModel>
        {
            new() { Id = "a81d9ab9277c7fdbb", Type = "subagent", Status = "running", Description = "Iteration-8 plan fix pass", AgentType = "general-purpose" },
        };
        var existing = CreateModel() with { RunningTasks = existingTasks };
        var newState = CreateModel() with { RunningTasks = newTasks };

        var result = FieldPreservation.PreserveFields(newState, existing);

        result.RunningTasks.Should().BeSameAs(newTasks);
    }

    [Fact]
    public void PreserveFields_RunningTasks_EmptyListOverwritesExisting()
    {
        // The empty roster ([]) means "measured: everything finished" and must
        // overwrite a prior non-empty roster rather than being normalised to null
        // and falling back to `existing` via `??`. If a future change "helpfully"
        // normalises [] to null on the write side, this test starts asserting the
        // stale non-empty roster survived instead of the fresh empty measurement —
        // that failure IS the regression this test exists to catch.
        var existingTasks = new List<BackgroundTaskModel>
        {
            new() { Id = "ac49354784c62a78e", Type = "subagent", Status = "running", Description = "Backfill three scope exclusions to idea.md", AgentType = "general-purpose" },
        };
        var existing = CreateModel() with { RunningTasks = existingTasks };
        var newState = CreateModel() with { RunningTasks = [] };

        var result = FieldPreservation.PreserveFields(newState, existing);

        result.RunningTasks.Should().NotBeNull();
        result.RunningTasks.Should().BeEmpty();
    }

    [Fact]
    public void ResolveLastMessage_PromptTakesPriority()
    {
        var result = FieldPreservation.ResolveLastMessage("the prompt", "the message", "previous");
        result.Should().Be("the prompt");
    }

    [Fact]
    public void ResolveLastMessage_MessageSecondPriority()
    {
        var result = FieldPreservation.ResolveLastMessage(null, "the message", "previous");
        result.Should().Be("the message");
    }

    [Fact]
    public void ResolveLastMessage_FallsToPrevious()
    {
        var result = FieldPreservation.ResolveLastMessage(null, null, "previous");
        result.Should().Be("previous");
    }

    [Fact]
    public void ResolveLastMessage_AllNull_ReturnsEmpty()
    {
        var result = FieldPreservation.ResolveLastMessage(null, null, null);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ResolveLastMessage_EmptyPrompt_FallsToMessage()
    {
        var result = FieldPreservation.ResolveLastMessage("", "the message", "previous");
        result.Should().Be("the message");
    }

    [Fact]
    public void ResolveLastMessage_TruncatesLongPrompt()
    {
        var longPrompt = new string('x', 200);
        var result = FieldPreservation.ResolveLastMessage(longPrompt, null, null);
        result.Should().HaveLength(120);
    }

    [Fact]
    public void TruncateMessage_ShortMessage_Unchanged()
    {
        StateFileModel.TruncateMessage("short").Should().Be("short");
    }

    [Fact]
    public void TruncateMessage_ExactLength_Unchanged()
    {
        var exact = new string('a', 120);
        StateFileModel.TruncateMessage(exact).Should().HaveLength(120);
    }

    [Fact]
    public void TruncateMessage_LongMessage_Truncated()
    {
        var long_ = new string('b', 200);
        StateFileModel.TruncateMessage(long_).Should().HaveLength(120);
    }

    [Fact]
    public void TruncateMessage_Null_ReturnsEmpty()
    {
        StateFileModel.TruncateMessage(null).Should().BeEmpty();
    }

    [Fact]
    public void TruncateMessage_Empty_ReturnsEmpty()
    {
        StateFileModel.TruncateMessage("").Should().BeEmpty();
    }

    [Fact]
    public void TruncateMessage_CustomMaxLength()
    {
        StateFileModel.TruncateMessage("hello world", 5).Should().Be("hello");
    }
}
