using FluentAssertions;
using Imrdy.Core.Display;
using Imrdy.Core.Workspace;

namespace Imrdy.Core.Tests.Display;

public class WorkspaceDashboardViewModelBuilderTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 5, 16, 12, 0, 0, TimeSpan.Zero);

    private static WorkspaceEntry MakeEntry(
        string path = @"D:\dev\myproject",
        string name = "MyProject",
        int desktop = 1,
        string? iconStyle = null) => new()
    {
        Path = path,
        Name = name,
        Desktop = desktop,
        IconStyle = iconStyle,
    };

    [Fact]
    public void Build_SetsName_FromEntry()
    {
        var entry = MakeEntry(name: "Proj");

        var vm = WorkspaceDashboardViewModelBuilder.Build(entry, null, null, null, FixedNow);

        vm.Name.Should().Be("Proj");
    }

    [Fact]
    public void Build_SetsPath_FromEntry()
    {
        var entry = MakeEntry(path: @"C:\dev\alpha");

        var vm = WorkspaceDashboardViewModelBuilder.Build(entry, null, null, null, FixedNow);

        vm.WorkspacePath.Should().Be(@"C:\dev\alpha");
    }

    [Fact]
    public void Build_SetsDesktop_FromEntry()
    {
        var entry = MakeEntry(desktop: 3);

        var vm = WorkspaceDashboardViewModelBuilder.Build(entry, null, null, null, FixedNow);

        vm.Desktop.Should().Be(3);
    }

    [Fact]
    public void Build_SetsIconStyle_Null_WhenEntryHasNone()
    {
        var entry = MakeEntry(iconStyle: null);

        var vm = WorkspaceDashboardViewModelBuilder.Build(entry, null, null, null, FixedNow);

        vm.IconStyle.Should().BeNull();
    }

    [Fact]
    public void Build_SetsIconStyle_FromEntry_WhenPresent()
    {
        var entry = MakeEntry(iconStyle: "diamonds");

        var vm = WorkspaceDashboardViewModelBuilder.Build(entry, null, null, null, FixedNow);

        vm.IconStyle.Should().Be("diamonds");
    }

    [Fact]
    public void Build_SetsActivityText_NeverSeen_WhenLastSeenAtNull()
    {
        var entry = MakeEntry();

        var vm = WorkspaceDashboardViewModelBuilder.Build(entry, null, null, null, FixedNow);

        vm.ActivityText.Should().Be("never seen");
    }

    [Fact]
    public void Build_SetsActivityText_AgoString_WhenLastSeenAtProvided()
    {
        // now=12:00:00, lastSeenAt=06:20:00 → span=5h 40m → "active 5h 40m ago"
        var lastSeenAt = new DateTimeOffset(2026, 5, 16, 6, 20, 0, TimeSpan.Zero);
        var entry = MakeEntry();

        var vm = WorkspaceDashboardViewModelBuilder.Build(entry, null, null, lastSeenAt, FixedNow);

        vm.ActivityText.Should().Be("active 5h 40m ago");
    }

    [Fact]
    public void Build_ActivityText_IsDeterministic_AcrossCalls()
    {
        var lastSeenAt = new DateTimeOffset(2026, 5, 16, 6, 20, 0, TimeSpan.Zero);
        var entry = MakeEntry();

        var vm1 = WorkspaceDashboardViewModelBuilder.Build(entry, null, null, lastSeenAt, FixedNow);
        var vm2 = WorkspaceDashboardViewModelBuilder.Build(entry, null, null, lastSeenAt, FixedNow);

        vm1.ActivityText.Should().Be(vm2.ActivityText);
    }

    [Fact]
    public void Build_SetsGit_FromCache_WhenProvided()
    {
        var git = new GitInfo("main", 2);
        var entry = MakeEntry();

        var vm = WorkspaceDashboardViewModelBuilder.Build(entry, git, null, null, FixedNow);

        vm.Git.Should().Be(git);
    }

    [Fact]
    public void Build_SetsGit_Null_WhenCacheMisses()
    {
        var entry = MakeEntry();

        var vm = WorkspaceDashboardViewModelBuilder.Build(entry, null, null, null, FixedNow);

        vm.Git.Should().BeNull();
    }

    [Fact]
    public void Build_IsCurrentDesktop_True_WhenIndicesMatch()
    {
        var entry = MakeEntry(desktop: 2);

        var vm = WorkspaceDashboardViewModelBuilder.Build(entry, null, currentDesktopIndex: 2, null, FixedNow);

        vm.IsCurrentDesktop.Should().BeTrue();
    }

    [Fact]
    public void Build_IsCurrentDesktop_False_WhenIndicesDiffer()
    {
        var entry = MakeEntry(desktop: 2);

        var vm = WorkspaceDashboardViewModelBuilder.Build(entry, null, currentDesktopIndex: 3, null, FixedNow);

        vm.IsCurrentDesktop.Should().BeFalse();
    }

    [Fact]
    public void Build_IsCurrentDesktop_False_WhenCurrentIndexNull()
    {
        var entry = MakeEntry(desktop: 1);

        var vm = WorkspaceDashboardViewModelBuilder.Build(entry, null, currentDesktopIndex: null, null, FixedNow);

        vm.IsCurrentDesktop.Should().BeFalse();
    }
}
