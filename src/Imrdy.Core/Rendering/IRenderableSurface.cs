using Microsoft.Extensions.Logging;

namespace Imrdy.Core.Rendering;

public interface IRenderableSurface
{
    /// <summary>Verb sub-argument, e.g. "dashboard", "overlay". Must be [a-z0-9-]+.</summary>
    string Name { get; }

    /// <summary>One-line description for --list output.</summary>
    string Description { get; }

    /// <summary>
    /// Relative to the dev-build repo root, e.g. "tests/fixtures/dashboards".
    /// Used by --all to enumerate fixtures. Null if the surface isn't fixture-driven
    /// (e.g., tray-icon takes status/style flags instead).
    /// </summary>
    string? DefaultFixtureDir { get; }

    /// <summary>Default output file extension: "png" for bitmaps, "json" for trees.</summary>
    string DefaultOutputExtension { get; }

    /// <summary>Parses component-specific args (after --) and produces an artifact.</summary>
    RenderResult Render(RenderContext context);
}
