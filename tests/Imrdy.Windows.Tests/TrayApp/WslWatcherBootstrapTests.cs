using FluentAssertions;
using Imrdy.Windows;
using Xunit;

namespace Imrdy.Windows.Tests.TrayApp;

/// <summary>
/// Unit tests for <see cref="WslWatcherBootstrap.EnumerateExistingStateFiles"/>.
/// </summary>
public class WslWatcherBootstrapTests
{
    [Fact]
    public void EnumerateExistingStateFiles_ReturnsAllJsonFiles()
    {
        var temp = Path.Combine(Path.GetTempPath(), "wslboot-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temp);
            File.WriteAllText(Path.Combine(temp, "a.json"), "{}");
            File.WriteAllText(Path.Combine(temp, "b.json"), "{}");
            File.WriteAllText(Path.Combine(temp, "ignore.txt"), "ignored");

            var result = WslWatcherBootstrap.EnumerateExistingStateFiles(temp);

            result.Should().HaveCount(2);
            result.Should().ContainSingle(p => p.EndsWith("a.json"));
            result.Should().ContainSingle(p => p.EndsWith("b.json"));
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void EnumerateExistingStateFiles_ReturnsEmpty_WhenDirectoryMissing()
    {
        var missing = Path.Combine(Path.GetTempPath(), "wslboot-missing-" + Guid.NewGuid().ToString("N"));

        var result = WslWatcherBootstrap.EnumerateExistingStateFiles(missing);

        result.Should().BeEmpty();
    }

    [Fact]
    public void EnumerateExistingStateFiles_ReturnsEmpty_WhenDirectoryHasNoJsonFiles()
    {
        var temp = Path.Combine(Path.GetTempPath(), "wslboot-nojson-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temp);
            File.WriteAllText(Path.Combine(temp, "readme.txt"), "nothing here");

            var result = WslWatcherBootstrap.EnumerateExistingStateFiles(temp);

            result.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }
}
