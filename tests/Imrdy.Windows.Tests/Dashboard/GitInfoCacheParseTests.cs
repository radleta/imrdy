using FluentAssertions;
using Imrdy.Windows.Dashboard;
using Xunit;

namespace Imrdy.Windows.Tests.Dashboard;

/// <summary>
/// Unit tests for <see cref="GitInfoCache.ParseGitOutput"/> — covers all five
/// <c>git status --porcelain --branch</c> header shapes that real repos produce.
/// Each fixture uses realistic multi-line porcelain v1 output so the parser sees
/// the full format it receives from the shell-out.
/// </summary>
public class GitInfoCacheParseTests
{
    // ---- helpers ----

    /// <summary>Builds a porcelain v1 string from a header line plus optional dirty-file lines.</summary>
    private static string Porcelain(string header, params string[] fileLines)
        => string.Join("\n", new[] { header }.Concat(fileLines));

    // ---- decision-table cases ----

    [Fact]
    public void ParseGitOutput_BranchOnlyNoRemote_AheadBehindZero()
    {
        // "## main" — no remote tracking branch at all
        var output = Porcelain("## main", " M src/Imrdy.Windows/Program.cs", "?? newfile.cs");

        var result = GitInfoCache.ParseGitOutput(output);

        result.Should().NotBeNull();
        result!.Branch.Should().Be("main");
        result.Ahead.Should().Be(0);
        result.Behind.Should().Be(0);
        result.DirtyCount.Should().Be(1, "only the ' M' line counts; '??' is skipped");
    }

    [Fact]
    public void ParseGitOutput_BranchWithRemoteNoDivergence_AheadBehindZero()
    {
        // "## main...origin/main" — tracking, no ahead/behind tokens
        var output = Porcelain("## main...origin/main", " M src/Imrdy.Core/Display/HookAccumulation.cs");

        var result = GitInfoCache.ParseGitOutput(output);

        result.Should().NotBeNull();
        result!.Branch.Should().Be("main");
        result.Ahead.Should().Be(0);
        result.Behind.Should().Be(0);
        result.DirtyCount.Should().Be(1);
    }

    [Fact]
    public void ParseGitOutput_AheadOnly_ParsesCorrectly()
    {
        // "## main...origin/main [ahead 2]"
        var output = Porcelain("## main...origin/main [ahead 2]",
            " M src/Imrdy.Windows/Dashboard/GitInfoCache.cs",
            "?? scratch/notes.md");

        var result = GitInfoCache.ParseGitOutput(output);

        result.Should().NotBeNull();
        result!.Branch.Should().Be("main");
        result.Ahead.Should().Be(2);
        result.Behind.Should().Be(0);
        result.DirtyCount.Should().Be(1, "the '??' untracked line is excluded");
    }

    [Fact]
    public void ParseGitOutput_BehindOnly_ParsesCorrectly()
    {
        // "## main...origin/main [behind 1]"
        var output = Porcelain("## main...origin/main [behind 1]",
            " M src/Imrdy.Core/Display/HookAccumulation.cs");

        var result = GitInfoCache.ParseGitOutput(output);

        result.Should().NotBeNull();
        result!.Branch.Should().Be("main");
        result.Ahead.Should().Be(0);
        result.Behind.Should().Be(1);
        result.DirtyCount.Should().Be(1);
    }

    [Fact]
    public void ParseGitOutput_AheadAndBehindCommaSeparated_ParsesBothCorrectly()
    {
        // "## main...origin/main [ahead 2, behind 1]" — both tokens in one bracket pair
        var output = Porcelain("## main...origin/main [ahead 2, behind 1]",
            " M src/Imrdy.Windows/Dashboard/GitInfoCache.cs",
            " M src/Imrdy.Core/Display/HookAccumulation.cs");

        var result = GitInfoCache.ParseGitOutput(output);

        result.Should().NotBeNull();
        result!.Branch.Should().Be("main");
        result.Ahead.Should().Be(2);
        result.Behind.Should().Be(1);
        result.DirtyCount.Should().Be(2);
    }
}
