namespace Imrdy.Integration.Tests.Helpers;

/// <summary>
/// Locates the dashboard fixture corpus under <c>tests/fixtures/dashboards/</c> and exposes
/// it as xunit MemberData. Shared by <see cref="FixtureCorpusRoundtripTests"/> and
/// <see cref="PreviewAllFixturesTests"/> to avoid duplicating directory-resolution logic.
/// </summary>
public static class FixtureCorpus
{
    public static readonly string FixtureDir = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "tests", "fixtures", "dashboards"));

    /// <summary>
    /// Returns one row per <c>*.json</c> file: <c>{ fileName, fullPath }</c>.
    /// Throws <see cref="DirectoryNotFoundException"/> when the fixture directory is absent
    /// so Theory tests fail explicitly rather than silently producing zero test cases.
    /// </summary>
    public static IEnumerable<object[]> FixtureFiles()
    {
        if (!Directory.Exists(FixtureDir))
            throw new DirectoryNotFoundException(
                $"Dashboard fixture directory not found: '{FixtureDir}'. " +
                "Ensure tests/fixtures/dashboards/ is present and populated before running integration tests.");

        foreach (var file in Directory.GetFiles(FixtureDir, "*.json").OrderBy(f => f))
            yield return new object[] { Path.GetFileName(file), file };
    }
}
