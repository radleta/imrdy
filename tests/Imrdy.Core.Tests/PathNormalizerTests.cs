using Imrdy.Core.Desktop;
using FluentAssertions;

namespace Imrdy.Core.Tests;

public class PathNormalizerTests
{
    [Theory]
    [InlineData("/d/dev/github/foo", @"D:\dev\github\foo")]
    [InlineData("/c/Users/me/projects", @"C:\Users\me\projects")]
    [InlineData("/D/dev/test", @"D:\dev\test")]
    public void Normalize_MsysPaths_ConvertToWindows(string input, string expected)
    {
        PathNormalizer.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(@"D:\dev\github\foo", @"D:\dev\github\foo")]
    [InlineData(@"C:\Users\me\projects", @"C:\Users\me\projects")]
    public void Normalize_WindowsPaths_PassThrough(string input, string expected)
    {
        PathNormalizer.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("D:/dev/github/foo", @"D:\dev\github\foo")]
    [InlineData("D:/dev/mixed\\path", @"D:\dev\mixed\path")]
    public void Normalize_MixedSlashes_Normalized(string input, string expected)
    {
        PathNormalizer.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(@"D:\dev\github\foo\", @"D:\dev\github\foo")]
    [InlineData(@"D:\dev\github\foo\\", @"D:\dev\github\foo")]
    [InlineData("/d/dev/foo/", @"D:\dev\foo")]
    public void Normalize_TrailingSlashes_Removed(string input, string expected)
    {
        PathNormalizer.Normalize(input).Should().Be(expected);
    }

    [Fact]
    public void Normalize_SingleDriveLetter_MsysRoot()
    {
        PathNormalizer.Normalize("/d").Should().Be(@"D:\");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Normalize_EmptyOrWhitespace_ReturnsEmpty(string? input)
    {
        PathNormalizer.Normalize(input!).Should().BeEmpty();
    }

    [Fact]
    public void AreEqual_SamePath_DifferentFormats_ReturnsTrue()
    {
        PathNormalizer.AreEqual("/d/dev/foo", @"D:\dev\foo").Should().BeTrue();
    }

    [Fact]
    public void AreEqual_SamePath_DifferentCase_ReturnsTrue()
    {
        PathNormalizer.AreEqual(@"D:\Dev\Foo", @"d:\dev\foo").Should().BeTrue();
    }

    [Fact]
    public void AreEqual_DifferentPaths_ReturnsFalse()
    {
        PathNormalizer.AreEqual(@"D:\dev\foo", @"D:\dev\bar").Should().BeFalse();
    }

    [Fact]
    public void AreEqual_WithTrailingSlash_ReturnsTrue()
    {
        PathNormalizer.AreEqual(@"D:\dev\foo\", @"D:\dev\foo").Should().BeTrue();
    }

    [Theory]
    [InlineData(@"D:\dev\github\foo", "foo")]
    [InlineData("/d/dev/github/bar", "bar")]
    [InlineData("D:/projects/my-app", "my-app")]
    public void DeriveProject_ExtractsLastComponent(string path, string expected)
    {
        PathNormalizer.DeriveProject(path).Should().Be(expected);
    }

    [Fact]
    public void DeriveProject_EmptyPath_ReturnsEmpty()
    {
        PathNormalizer.DeriveProject("").Should().BeEmpty();
    }
}
