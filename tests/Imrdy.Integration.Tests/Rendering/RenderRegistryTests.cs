using System.Text.RegularExpressions;
using FluentAssertions;
using Imrdy.Windows.Rendering;
using Xunit;

namespace Imrdy.Integration.Tests.Rendering;

/// <summary>
/// Registry integration tests — registry lives in Imrdy.Windows, so these belong here.
/// </summary>
[Trait("Category", "Integration")]
public class RenderRegistryTests
{
    [Fact]
    public void Components_IsNonEmpty()
    {
        RenderRegistry.Components.Should().NotBeEmpty();
    }

    [Fact]
    public void Components_ContainsDashboard()
    {
        RenderRegistry.Components.Should().Contain(c => c.Name == "dashboard");
    }

    [Fact]
    public void Components_AllHaveNonEmptyRequiredFields()
    {
        foreach (var c in RenderRegistry.Components)
        {
            c.Name.Should().NotBeNullOrWhiteSpace($"component {c.Name} must have a non-empty Name");
            c.Description.Should().NotBeNullOrWhiteSpace($"component {c.Name} must have a non-empty Description");
            c.DefaultOutputExtension.Should().NotBeNullOrWhiteSpace($"component {c.Name} must have a non-empty DefaultOutputExtension");
        }
    }

    [Fact]
    public void Components_AllNamesMatchSlugPattern()
    {
        var pattern = new Regex("^[a-z0-9-]+$");
        foreach (var c in RenderRegistry.Components)
            c.Name.Should().MatchRegex(pattern.ToString(), $"component name '{c.Name}' must match ^[a-z0-9-]+$");
    }

    [Fact]
    public void Components_NamesAreUnique()
    {
        var names = RenderRegistry.Components.Select(c => c.Name).ToList();
        names.Should().OnlyHaveUniqueItems("component names must be unique in the registry");
    }
}
