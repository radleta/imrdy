using Imrdy.Core.State;
using Imrdy.Core.Workspace;
using FluentAssertions;

namespace Imrdy.Core.Tests;

public class WorkspaceVisibilityTests
{
    private readonly WorkspaceVisibility _visibility = new();

    private static WorkspaceEntry MakeWorkspace(string path, string name = "test", int desktop = 1)
        => new() { Path = path, Name = name, Desktop = desktop };

    private static StateFileModel MakeSession(
        string cwd, int? desktopIndex = null, string sessionId = "s1",
        DateTimeOffset? timestamp = null)
        => new()
        {
            SessionId = sessionId,
            Status = "busy",
            Project = "test",
            Cwd = cwd,
            HookEvent = "UserPromptSubmit",
            DesktopIndex = desktopIndex,
            Timestamp = timestamp ?? DateTimeOffset.UtcNow,
        };

    [Fact]
    public void Evaluate_NoActiveSessions_WorkspaceVisible()
    {
        var workspaces = new[] { MakeWorkspace(@"D:\dev\project") };

        var results = _visibility.Evaluate(workspaces, []);

        results.Should().HaveCount(1);
        results[0].IsVisible.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_ActiveSessionMatches_WorkspaceHidden()
    {
        var workspaces = new[] { MakeWorkspace(@"D:\dev\project") };
        var sessions = new[] { MakeSession(@"D:\dev\project") };

        var results = _visibility.Evaluate(workspaces, sessions);

        results[0].IsVisible.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_ActiveSessionDifferentPath_WorkspaceVisible()
    {
        var workspaces = new[] { MakeWorkspace(@"D:\dev\project-a") };
        var sessions = new[] { MakeSession(@"D:\dev\project-b") };

        var results = _visibility.Evaluate(workspaces, sessions);

        results[0].IsVisible.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_MultipleSessionsMatch_StillHidden()
    {
        var workspaces = new[] { MakeWorkspace(@"D:\dev\project") };
        var sessions = new[]
        {
            MakeSession(@"D:\dev\project", sessionId: "s1"),
            MakeSession(@"D:\dev\project", sessionId: "s2"),
        };

        var results = _visibility.Evaluate(workspaces, sessions);

        results[0].IsVisible.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_DesktopAutoTracked_FromLatestSession()
    {
        var workspaces = new[] { MakeWorkspace(@"D:\dev\project", desktop: 1) };
        var baseTime = DateTimeOffset.UtcNow;
        var sessions = new[]
        {
            MakeSession(@"D:\dev\project", desktopIndex: 2, sessionId: "s1",
                timestamp: baseTime),
            MakeSession(@"D:\dev\project", desktopIndex: 3, sessionId: "s2",
                timestamp: baseTime + TimeSpan.FromMinutes(1)),
        };

        var results = _visibility.Evaluate(workspaces, sessions);

        results[0].TrackedDesktop.Should().Be(3);
    }

    [Fact]
    public void Evaluate_NoDesktopOnSessions_FallsBackToWorkspaceDesktop()
    {
        var workspaces = new[] { MakeWorkspace(@"D:\dev\project", desktop: 2) };
        var sessions = new[] { MakeSession(@"D:\dev\project", desktopIndex: null) };

        var results = _visibility.Evaluate(workspaces, sessions);

        results[0].TrackedDesktop.Should().Be(2);
    }

    [Fact]
    public void Evaluate_HiddenToVisible_DesktopChanged_FlagsChange()
    {
        var workspaces = new[] { MakeWorkspace(@"D:\dev\project", desktop: 1) };

        // First: session active → hidden, tracking desktop 3
        var sessions = new[] { MakeSession(@"D:\dev\project", desktopIndex: 3) };
        _visibility.Evaluate(workspaces, sessions);

        // Second: session ends → visible, tracked desktop (3) differs from workspace (1)
        var results = _visibility.Evaluate(workspaces, []);

        results[0].IsVisible.Should().BeTrue();
        results[0].DesktopChanged.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_VisibleToVisible_NoDesktopChange()
    {
        var workspaces = new[] { MakeWorkspace(@"D:\dev\project", desktop: 1) };

        // Both evaluations: no sessions, stays visible
        _visibility.Evaluate(workspaces, []);
        var results = _visibility.Evaluate(workspaces, []);

        results[0].DesktopChanged.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_HiddenToVisible_SameDesktop_NoChange()
    {
        var workspaces = new[] { MakeWorkspace(@"D:\dev\project", desktop: 2) };

        // Hidden with desktop 2 (same as workspace)
        var sessions = new[] { MakeSession(@"D:\dev\project", desktopIndex: 2) };
        _visibility.Evaluate(workspaces, sessions);

        // Visible again — desktop hasn't changed
        var results = _visibility.Evaluate(workspaces, []);

        results[0].DesktopChanged.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_MultipleWorkspaces_IndependentVisibility()
    {
        var workspaces = new[]
        {
            MakeWorkspace(@"D:\dev\project-a", "a"),
            MakeWorkspace(@"D:\dev\project-b", "b"),
        };
        var sessions = new[] { MakeSession(@"D:\dev\project-a") };

        var results = _visibility.Evaluate(workspaces, sessions);

        results[0].IsVisible.Should().BeFalse();
        results[1].IsVisible.Should().BeTrue();
    }

    [Fact]
    public void Clear_ResetsState()
    {
        var workspaces = new[] { MakeWorkspace(@"D:\dev\project") };
        var sessions = new[] { MakeSession(@"D:\dev\project", desktopIndex: 5) };

        _visibility.Evaluate(workspaces, sessions);
        _visibility.Clear();

        // After clear, hidden→visible transition is not detected (starts fresh)
        var results = _visibility.Evaluate(workspaces, []);
        results[0].DesktopChanged.Should().BeFalse();
    }
}
