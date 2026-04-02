using Imrdy.Core.Hooks;
using Imrdy.Core.State;
using FluentAssertions;

namespace Imrdy.Core.Tests;

public class FieldPreservationTests
{
    private static StateFileModel CreateModel(
        string? soundPack = null,
        int? desktopIndex = null,
        string lastMessage = "") => new()
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
    public void PreserveFields_BothNull_StaysNull()
    {
        var existing = CreateModel();
        var newState = CreateModel();

        var result = FieldPreservation.PreserveFields(newState, existing);

        result.SoundPack.Should().BeNull();
        result.DesktopIndex.Should().BeNull();
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
