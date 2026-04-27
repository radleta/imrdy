using System.Text.Json;
using FluentAssertions;
using Imrdy.Core;
using Imrdy.Core.Hooks;
using Imrdy.Core.State;

namespace Imrdy.Core.Tests.State;

public class StateFileModelStartedAtTests
{
    private static StateFileModel CreateModel(DateTimeOffset? startedAt = null) => new()
    {
        SessionId = "s1",
        Status = "busy",
        Project = "imrdy",
        Cwd = @"D:\dev\imrdy",
        HookEvent = "SessionStart",
        Timestamp = new DateTimeOffset(2026, 4, 24, 12, 0, 0, TimeSpan.Zero),
        StartedAt = startedAt,
    };

    [Fact]
    public void StartedAt_SerializesWithSnakeCaseKey()
    {
        var startedAt = new DateTimeOffset(2026, 4, 24, 11, 30, 0, TimeSpan.Zero);
        var model = CreateModel(startedAt);

        var json = JsonSerializer.Serialize(model, ImrdyJsonContext.Default.StateFileModel);

        json.Should().Contain("\"started_at\":");
    }

    [Fact]
    public void StartedAt_RoundTripsThroughJson()
    {
        var startedAt = new DateTimeOffset(2026, 4, 24, 11, 30, 0, TimeSpan.Zero);
        var model = CreateModel(startedAt);

        var json = JsonSerializer.Serialize(model, ImrdyJsonContext.Default.StateFileModel);
        var parsed = JsonSerializer.Deserialize(json, ImrdyJsonContext.Default.StateFileModel);

        parsed.Should().NotBeNull();
        parsed!.StartedAt.Should().Be(startedAt);
    }

    [Fact]
    public void StartedAt_NullByDefault()
    {
        var model = CreateModel(startedAt: null);

        model.StartedAt.Should().BeNull();
    }

    [Fact]
    public void StartedAt_NullDeserializesFromMissingJsonKey()
    {
        var json = """
            {
              "session_id": "s1",
              "status": "busy",
              "project": "imrdy",
              "cwd": "D:\\dev\\imrdy",
              "hook_event": "SessionStart",
              "timestamp": "2026-04-24T12:00:00+00:00"
            }
            """;

        var parsed = JsonSerializer.Deserialize(json, ImrdyJsonContext.Default.StateFileModel);

        parsed.Should().NotBeNull();
        parsed!.StartedAt.Should().BeNull();
    }

    [Fact]
    public void FieldPreservation_PreservesStartedAtFromExisting()
    {
        var existing = CreateModel(new DateTimeOffset(2026, 4, 24, 10, 0, 0, TimeSpan.Zero));
        var newState = CreateModel(startedAt: null);

        var result = FieldPreservation.PreserveFields(newState, existing);

        result.StartedAt.Should().Be(existing.StartedAt);
    }

    [Fact]
    public void FieldPreservation_NewStartedAtTakesPrecedence()
    {
        var older = new DateTimeOffset(2026, 4, 24, 10, 0, 0, TimeSpan.Zero);
        var newer = new DateTimeOffset(2026, 4, 24, 11, 0, 0, TimeSpan.Zero);
        var existing = CreateModel(older);
        var newState = CreateModel(newer);

        var result = FieldPreservation.PreserveFields(newState, existing);

        result.StartedAt.Should().Be(newer);
    }

    [Fact]
    public void FieldPreservation_BothNull_StaysNull()
    {
        var existing = CreateModel(startedAt: null);
        var newState = CreateModel(startedAt: null);

        var result = FieldPreservation.PreserveFields(newState, existing);

        result.StartedAt.Should().BeNull();
    }

    [Fact]
    public void MaxMessageLength_IsAccessibleAsInternalConstant()
    {
        // The constant was promoted from `private const int` to `internal const int` so
        // tests (with InternalsVisibleTo) can assert truncation behavior against it.
        StateFileModel.MaxMessageLength.Should().Be(120);
    }
}
