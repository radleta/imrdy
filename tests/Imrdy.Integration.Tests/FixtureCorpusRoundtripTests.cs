using System.Text.Json;
using FluentAssertions;
using Imrdy.Core;
using Imrdy.Core.Display;
using Imrdy.Integration.Tests.Helpers;
using Xunit;

namespace Imrdy.Integration.Tests;

/// <summary>
/// Schema-lock regression suite. Deserializes every fixture via <see cref="ImrdyJsonContext"/>
/// (source-generated, no reflection), re-serializes, then deserializes again and asserts
/// structural equivalence. Locking both the schema shape AND the source-gen configuration.
/// </summary>
[Trait("Category", "Integration")]
public class FixtureCorpusRoundtripTests
{
    public static IEnumerable<object[]> FixtureFiles() => FixtureCorpus.FixtureFiles();

    [Theory]
    [MemberData(nameof(FixtureFiles))]
    public void FixtureRoundTrips_ViaImrdyJsonContext(string fixtureName, string fixturePath)
    {
        _ = fixtureName; // used as Theory display name

        File.Exists(fixturePath).Should().BeTrue($"fixture must exist at '{fixturePath}'");

        var json = File.ReadAllText(fixturePath);

        // Deserialize via source-gen (no reflection) — this is the schema lock assertion.
        var vm = JsonSerializer.Deserialize(json, ImrdyJsonContext.Default.DashboardViewModel);
        vm.Should().NotBeNull($"fixture '{fixtureName}' must deserialize to a valid DashboardViewModel");

        // Re-serialize via source-gen.
        var roundTripped = JsonSerializer.Serialize(vm, ImrdyJsonContext.Default.DashboardViewModel);
        roundTripped.Should().NotBeNullOrWhiteSpace();

        // Deserialize the re-serialized JSON back and compare structurally.
        var vm2 = JsonSerializer.Deserialize(roundTripped, ImrdyJsonContext.Default.DashboardViewModel);
        vm2.Should().BeEquivalentTo(vm,
            $"fixture '{fixtureName}' must survive a full serialize → deserialize round-trip without data loss");
    }
}
