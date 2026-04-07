using FluentAssertions;
using Imrdy.Integration.Tests.Helpers;
using Xunit;

namespace Imrdy.Integration.Tests;

[Trait("Category", "Integration")]
public class PacksIntegrationTests : IDisposable
{
    private readonly CliTestFixture _cli = new();
    private readonly TempDirectoryFixture _temp = new();

    private static string RepoRoot => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public async Task PacksList_ExitsZero()
    {
        var (exitCode, _, _) = await _cli.RunAsync("packs list", workingDirectory: _temp.Path);

        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task PacksPack_CreatesZipFile()
    {
        var soundsDir = Path.Combine(RepoRoot, "sounds", "assistant");
        Directory.Exists(soundsDir).Should().BeTrue($"sounds/assistant should exist at {soundsDir}");

        var outputDir = Path.Combine(_temp.Path, "output");
        Directory.CreateDirectory(outputDir);

        var (exitCode, stdout, stderr) = await _cli.RunAsync(
            $"packs pack \"{soundsDir}\" --output \"{outputDir}\"",
            workingDirectory: RepoRoot);

        exitCode.Should().Be(0, $"stdout: {stdout}\nstderr: {stderr}");

        var zipFiles = Directory.GetFiles(outputDir, "*.zip");
        zipFiles.Should().NotBeEmpty("a ZIP file should be created in the output directory");
    }

    [Fact]
    public async Task PacksRemove_NonexistentPack_ExitsOne()
    {
        var (exitCode, _, stderr) = await _cli.RunAsync("packs remove nonexistent", workingDirectory: _temp.Path);

        exitCode.Should().Be(1);
    }

    public void Dispose()
    {
        _temp.Dispose();
    }
}
