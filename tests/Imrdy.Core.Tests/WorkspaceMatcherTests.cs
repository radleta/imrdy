using Imrdy.Core.Workspace;
using FluentAssertions;

namespace Imrdy.Core.Tests;

public class WorkspaceMatcherTests
{
    private static WorkspaceEntry MakeWorkspace(string path, string name = "test", int desktop = 1)
        => new() { Path = path, Name = name, Desktop = desktop };

    [Fact]
    public void Match_ExactWindowsPath_ReturnsWorkspace()
    {
        var workspaces = new[] { MakeWorkspace(@"D:\dev\project", "project") };
        var result = WorkspaceMatcher.Match(workspaces, @"D:\dev\project");

        result.Should().NotBeNull();
        result!.Name.Should().Be("project");
    }

    [Fact]
    public void Match_MsysPath_MatchesWindowsPath()
    {
        var workspaces = new[] { MakeWorkspace(@"D:\dev\project", "project") };
        var result = WorkspaceMatcher.Match(workspaces, "/d/dev/project");

        result.Should().NotBeNull();
        result!.Name.Should().Be("project");
    }

    [Fact]
    public void Match_CaseInsensitive_Matches()
    {
        var workspaces = new[] { MakeWorkspace(@"D:\Dev\Project", "project") };
        var result = WorkspaceMatcher.Match(workspaces, @"d:\dev\project");

        result.Should().NotBeNull();
    }

    [Fact]
    public void Match_NestedPath_NoFalsePositive()
    {
        var workspaces = new[] { MakeWorkspace(@"D:\dev\project", "project") };
        var result = WorkspaceMatcher.Match(workspaces, @"D:\dev\project\subdir");

        result.Should().BeNull();
    }

    [Fact]
    public void Match_ParentPath_NoFalsePositive()
    {
        var workspaces = new[] { MakeWorkspace(@"D:\dev\project", "project") };
        var result = WorkspaceMatcher.Match(workspaces, @"D:\dev");

        result.Should().BeNull();
    }

    [Fact]
    public void Match_NoWorkspaces_ReturnsNull()
    {
        var result = WorkspaceMatcher.Match([], @"D:\dev\project");
        result.Should().BeNull();
    }

    [Fact]
    public void Match_EmptyCwd_ReturnsNull()
    {
        var workspaces = new[] { MakeWorkspace(@"D:\dev\project") };
        var result = WorkspaceMatcher.Match(workspaces, "");

        result.Should().BeNull();
    }

    [Fact]
    public void Match_NullCwd_ReturnsNull()
    {
        var workspaces = new[] { MakeWorkspace(@"D:\dev\project") };
        var result = WorkspaceMatcher.Match(workspaces, null!);

        result.Should().BeNull();
    }

    [Fact]
    public void Match_TrailingSlash_StillMatches()
    {
        var workspaces = new[] { MakeWorkspace(@"D:\dev\project", "project") };
        var result = WorkspaceMatcher.Match(workspaces, @"D:\dev\project\");

        result.Should().NotBeNull();
    }

    [Fact]
    public void Match_ForwardSlashes_StillMatches()
    {
        var workspaces = new[] { MakeWorkspace(@"D:\dev\project", "project") };
        var result = WorkspaceMatcher.Match(workspaces, "D:/dev/project");

        result.Should().NotBeNull();
    }

    [Fact]
    public void Match_MultipleWorkspaces_ReturnsCorrectOne()
    {
        var workspaces = new[]
        {
            MakeWorkspace(@"D:\dev\project-a", "project-a"),
            MakeWorkspace(@"D:\dev\project-b", "project-b"),
            MakeWorkspace(@"D:\dev\project-c", "project-c"),
        };

        var result = WorkspaceMatcher.Match(workspaces, @"D:\dev\project-b");
        result.Should().NotBeNull();
        result!.Name.Should().Be("project-b");
    }
}
