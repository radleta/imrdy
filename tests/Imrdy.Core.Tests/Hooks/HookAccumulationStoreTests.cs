using FluentAssertions;
using Imrdy.Core.Hooks;

namespace Imrdy.Core.Tests.Hooks;

public class HookAccumulationStoreTests
{
    private const string Sid = "s1";
    private static readonly DateTimeOffset BaseTime = new(2026, 4, 24, 12, 0, 0, TimeSpan.Zero);

    private static HookEventModel Evt(string name, string? toolName = null, string? agentId = null) => new()
    {
        HookEventName = name,
        SessionId = Sid,
        ToolName = toolName,
        AgentId = agentId,
    };

    private static HookAccumulationStore StoreAt(DateTimeOffset fixedTime)
    {
        var store = new HookAccumulationStore
        {
            NowProvider = () => fixedTime,
        };
        return store;
    }

    [Fact]
    public void GetSnapshot_UnknownSession_ReturnsEmpty()
    {
        var store = new HookAccumulationStore();

        var snap = store.GetSnapshot("unknown");

        snap.TurnCount.Should().Be(0);
        snap.FailureCount.Should().Be(0);
        snap.RecentTools.Should().BeEmpty();
        snap.ActivityTimestamps.Should().BeEmpty();
        snap.ActiveAgentIds.Should().BeEmpty();
        snap.CurrentTool.Should().BeNull();
        snap.PermissionTool.Should().BeNull();
    }

    [Fact]
    public void Apply_UserPromptSubmit_IncrementsTurnCount()
    {
        var store = new HookAccumulationStore();

        store.Apply(Evt("UserPromptSubmit"), "busy");
        store.Apply(Evt("UserPromptSubmit"), "busy");

        store.GetSnapshot(Sid).TurnCount.Should().Be(2);
    }

    [Fact]
    public void Apply_SessionStart_ResetsAllCounters()
    {
        var store = StoreAt(BaseTime);

        // Build up real state so the reset has something to clear.
        store.Apply(Evt("UserPromptSubmit"), "busy");
        store.Apply(Evt("UserPromptSubmit"), "busy");           // TurnCount = 2
        store.Apply(Evt("PostToolUseFailure"), "error");        // FailureCount = 1
        store.Apply(Evt("PreToolUse", toolName: "Bash"), "busy"); // CurrentTool = Bash
        store.Apply(Evt("PostToolUse", toolName: "Edit"), "idle"); // RecentTools has Edit, CurrentTool cleared by transition
        store.Apply(Evt("PreToolUse", toolName: "Write"), "permission"); // PermissionTool = Write
        store.Apply(Evt("PreToolUse", agentId: "a1"), "permission"); // ActiveAgentIds has a1
        store.Apply(Evt("PreToolUse", toolName: "Later"), "busy"); // CurrentTool = Later (transition out of permission clears PermissionTool first)

        // At this point the accumulator has non-default values for EVERY field.
        var before = store.GetSnapshot(Sid);
        before.TurnCount.Should().BeGreaterThan(0);
        before.FailureCount.Should().BeGreaterThan(0);
        before.RecentTools.Should().NotBeEmpty();
        before.ActivityTimestamps.Should().NotBeEmpty();
        before.ActiveAgentIds.Should().NotBeEmpty();
        before.CurrentTool.Should().NotBeNull();
        // PermissionTool was cleared by the transition away from permission — re-establish it.

        // Re-establish PermissionTool so the SessionStart reset actually has work to do for it.
        // Transition permission -> busy cleared it above; move current back into permission.
        store.Apply(Evt("PreToolUse", toolName: "Guarded"), "permission");
        store.GetSnapshot(Sid).PermissionTool.Should().Be("Guarded");

        // Act
        store.Apply(Evt("SessionStart"), "start");

        // Every field must be back to its empty default.
        var snap = store.GetSnapshot(Sid);
        snap.TurnCount.Should().Be(0);
        snap.FailureCount.Should().Be(0);
        snap.RecentTools.Should().BeEmpty();
        snap.ActivityTimestamps.Should().BeEmpty();
        snap.ActiveAgentIds.Should().BeEmpty();
        snap.CurrentTool.Should().BeNull();
        snap.PermissionTool.Should().BeNull();
    }

    [Fact]
    public void Apply_PostToolUseFailure_IncrementsFailureCount()
    {
        var store = new HookAccumulationStore();

        store.Apply(Evt("PostToolUseFailure"), "error");
        store.Apply(Evt("PostToolUseFailure"), "error");
        store.Apply(Evt("PermissionDenied"), "error");

        store.GetSnapshot(Sid).FailureCount.Should().Be(3);
    }

    [Fact]
    public void Apply_PostToolUse_DoesNotIncrementFailureCount()
    {
        var store = new HookAccumulationStore();

        store.Apply(Evt("PostToolUse", toolName: "Bash"), "idle");

        store.GetSnapshot(Sid).FailureCount.Should().Be(0);
    }

    [Fact]
    public void Apply_RecentTools_RingBufferCapsAtEight()
    {
        var store = new HookAccumulationStore();

        for (int i = 0; i < 10; i++)
        {
            store.Apply(Evt("PostToolUse", toolName: $"Tool{i}"), "idle");
        }

        var snap = store.GetSnapshot(Sid);
        snap.RecentTools.Should().HaveCount(8);
        snap.RecentTools.First().ToolName.Should().Be("Tool2", because: "oldest two were evicted");
        snap.RecentTools.Last().ToolName.Should().Be("Tool9");
    }

    [Fact]
    public void Apply_ActivityTimestamps_PopulatedByEveryNonSessionStartEvent()
    {
        // idea.md line 58: "Activity sparkline — hook-timestamp density over last 60s" —
        // every hook event except SessionStart contributes.
        var store = StoreAt(BaseTime);

        store.Apply(Evt("UserPromptSubmit"), "busy");
        store.Apply(Evt("PreToolUse", toolName: "Bash"), "busy");
        store.Apply(Evt("PostToolUse", toolName: "Bash"), "idle");

        store.GetSnapshot(Sid).ActivityTimestamps.Should().HaveCount(3);
    }

    [Fact]
    public void Apply_SessionStart_DoesNotAddActivityTimestamp()
    {
        var store = StoreAt(BaseTime);

        store.Apply(Evt("SessionStart"), "start");

        store.GetSnapshot(Sid).ActivityTimestamps.Should().BeEmpty();
    }

    [Fact]
    public void Apply_ActivityTimestamps_PrunedWhenNowAdvancesPast60s()
    {
        // Acceptance criterion: Apply with now+120s must prune entries outside 60s window.
        var store = new HookAccumulationStore();

        // Phase 1: inject t0, add 3 events so they land at t0.
        var t0 = BaseTime;
        store.NowProvider = () => t0;
        store.Apply(Evt("PreToolUse", toolName: "A"), "busy");
        store.Apply(Evt("PreToolUse", toolName: "B"), "busy");
        store.Apply(Evt("PreToolUse", toolName: "C"), "busy");
        store.GetSnapshot(Sid).ActivityTimestamps.Should().HaveCount(3);

        // Phase 2: advance clock to t0 + 120s (outside the 60s window) and fire one more event.
        // The prune should evict all 3 t0 entries; only the new t0+120s entry remains.
        var tFuture = t0 + TimeSpan.FromSeconds(120);
        store.NowProvider = () => tFuture;
        store.Apply(Evt("PreToolUse", toolName: "D"), "busy");

        var snap = store.GetSnapshot(Sid);
        snap.ActivityTimestamps.Should().HaveCount(1);
        snap.ActivityTimestamps[0].Should().Be(tFuture);
    }

    [Fact]
    public void Apply_ActivityTimestamps_KeepsEntriesWithin60s()
    {
        var store = new HookAccumulationStore();

        // Entry at t0.
        var t0 = BaseTime;
        store.NowProvider = () => t0;
        store.Apply(Evt("PreToolUse", toolName: "A"), "busy");

        // Advance to t0 + 45s — inside the 60s window. The t0 entry survives.
        var t1 = t0 + TimeSpan.FromSeconds(45);
        store.NowProvider = () => t1;
        store.Apply(Evt("PreToolUse", toolName: "B"), "busy");

        store.GetSnapshot(Sid).ActivityTimestamps.Should().HaveCount(2);
    }

    [Fact]
    public void Apply_AgentId_TracksDistinctAgentsSinceLastUserPromptSubmit()
    {
        var store = new HookAccumulationStore();

        store.Apply(Evt("PreToolUse", agentId: "a1"), "busy");
        store.Apply(Evt("PreToolUse", agentId: "a2"), "busy");
        store.Apply(Evt("PreToolUse", agentId: "a1"), "busy"); // duplicate

        store.GetSnapshot(Sid).ActiveAgentIds.Should().BeEquivalentTo(new[] { "a1", "a2" });
    }

    [Fact]
    public void Apply_UserPromptSubmit_ClearsActiveAgentIds()
    {
        var store = new HookAccumulationStore();
        store.Apply(Evt("PreToolUse", agentId: "a1"), "busy");
        store.Apply(Evt("PreToolUse", agentId: "a2"), "busy");

        store.Apply(Evt("UserPromptSubmit"), "busy");

        store.GetSnapshot(Sid).ActiveAgentIds.Should().BeEmpty();
    }

    [Fact]
    public void Apply_TransitionIntoBusy_SetsCurrentTool()
    {
        var store = new HookAccumulationStore();
        store.Apply(Evt("UserPromptSubmit"), "idle");

        store.Apply(Evt("PreToolUse", toolName: "Bash"), "busy");

        store.GetSnapshot(Sid).CurrentTool.Should().Be("Bash");
    }

    [Fact]
    public void Apply_TransitionAwayFromBusy_ClearsCurrentTool()
    {
        var store = new HookAccumulationStore();
        store.Apply(Evt("UserPromptSubmit"), "idle");
        store.Apply(Evt("PreToolUse", toolName: "Bash"), "busy");

        store.Apply(Evt("PostToolUse", toolName: "Bash"), "idle");

        store.GetSnapshot(Sid).CurrentTool.Should().BeNull();
    }

    [Fact]
    public void Apply_StayingInBusy_DoesNotChangeCurrentTool()
    {
        var store = new HookAccumulationStore();
        store.Apply(Evt("UserPromptSubmit"), "idle");
        store.Apply(Evt("PreToolUse", toolName: "Bash"), "busy");

        // Subsequent busy event with new tool name does NOT overwrite — only transitions matter.
        store.Apply(Evt("PreToolUse", toolName: "Edit"), "busy");

        store.GetSnapshot(Sid).CurrentTool.Should().Be("Bash");
    }

    [Fact]
    public void Apply_TransitionIntoPermission_SetsPermissionTool()
    {
        var store = new HookAccumulationStore();
        store.Apply(Evt("UserPromptSubmit"), "busy");

        store.Apply(Evt("PreToolUse", toolName: "Bash"), "permission");

        store.GetSnapshot(Sid).PermissionTool.Should().Be("Bash");
    }

    [Fact]
    public void Apply_TransitionAwayFromPermission_ClearsPermissionTool()
    {
        var store = new HookAccumulationStore();
        store.Apply(Evt("UserPromptSubmit"), "busy");
        store.Apply(Evt("PreToolUse", toolName: "Bash"), "permission");

        store.Apply(Evt("PostToolUse", toolName: "Bash"), "idle");

        store.GetSnapshot(Sid).PermissionTool.Should().BeNull();
    }

    [Fact]
    public void Apply_NullEvent_Throws()
    {
        var store = new HookAccumulationStore();

        var act = () => store.Apply(null!, "idle");

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Apply_EmptySessionId_Ignored()
    {
        var store = new HookAccumulationStore();
        var evt = new HookEventModel { HookEventName = "UserPromptSubmit", SessionId = "" };

        store.Apply(evt, "busy");

        store.SnapshotAll().Should().BeEmpty();
    }

    [Fact]
    public void SnapshotAll_ReturnsEverySession()
    {
        var store = new HookAccumulationStore();
        store.Apply(new HookEventModel { HookEventName = "UserPromptSubmit", SessionId = "s1" }, "busy");
        store.Apply(new HookEventModel { HookEventName = "UserPromptSubmit", SessionId = "s2" }, "busy");

        var all = store.SnapshotAll();

        all.Should().HaveCount(2);
        all.Should().ContainKey("s1");
        all.Should().ContainKey("s2");
    }
}
