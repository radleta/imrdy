using FluentAssertions;
using Imrdy.Core.Rendering;
using Microsoft.Extensions.Logging.Abstractions;

namespace Imrdy.Core.Tests.Rendering;

public class RenderContractsTests
{
    [Fact]
    public void RenderContext_RecordEquality_TwoInstancesWithSameArgsShouldBeEqual()
    {
        var args = new[] { "tests/fixtures/dashboards/fresh-idle.json" };
        var ctx1 = new RenderContext(args, "/tmp/out.png", NullLoggerFactory.Instance, "/repo");
        var ctx2 = new RenderContext(args, "/tmp/out.png", NullLoggerFactory.Instance, "/repo");

        ctx1.Should().Be(ctx2);
    }

    [Fact]
    public void RenderResult_SuccessTrueWithNonNullError_IsNotRejectedByType()
    {
        // Documents that RenderResult does NOT enforce Success=true → Error=null;
        // callers are responsible for checking both fields.
        var result = new RenderResult(Success: true, Error: "unexpected but valid at type level", Width: 460, Height: 284);

        result.Success.Should().BeTrue();
        result.Error.Should().NotBeNull();
    }

    [Fact]
    public void IRenderableSurface_DefaultFixtureDirNull_IsASupportedShape()
    {
        // A surface that isn't fixture-driven (e.g., tray-icon with flag inputs) returns null.
        IRenderableSurface surface = new FlagDrivenTestSurface();

        surface.DefaultFixtureDir.Should().BeNull();
    }

    // Test-only implementation that represents a non-fixture-driven surface.
    private sealed class FlagDrivenTestSurface : IRenderableSurface
    {
        public string Name => "test-flags";
        public string Description => "A test surface driven by flags, not fixtures.";
        public string? DefaultFixtureDir => null;
        public string DefaultOutputExtension => "png";
        public RenderResult Render(RenderContext context) =>
            new RenderResult(Success: true, Error: null, Width: 0, Height: 0);
    }
}
