using Imrdy.Core;
using Imrdy.Core.Graphics;
using Imrdy.Core.Icons;
using Microsoft.Extensions.Logging;

namespace Imrdy.Windows.Icons;

internal sealed class TrayIconRendererFactory
{
    private readonly GraphicsPackLoader _packLoader;
    private readonly ILoggerFactory _loggerFactory;

    public TrayIconRendererFactory(GraphicsPackLoader packLoader, ILoggerFactory loggerFactory)
    {
        _packLoader = packLoader;
        _loggerFactory = loggerFactory;
    }

    public ITrayIconRenderer Create(string? iconStyle)
    {
        var canonical = StyleNames.NormalizeStyleName(iconStyle) ?? "circles";

        if (canonical.StartsWith("pack:", StringComparison.OrdinalIgnoreCase))
        {
            var packName = canonical.Substring("pack:".Length);
            var graphicsPacksDirFull = Path.GetFullPath(ImrdyPaths.GraphicsPacksDir);
            var packDirFull = Path.GetFullPath(Path.Combine(graphicsPacksDirFull, packName));
            if (!packDirFull.StartsWith(graphicsPacksDirFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                _loggerFactory.CreateLogger<TrayIconRendererFactory>().LogWarning(
                    "Invalid pack name {PackName} — path traversal detected, falling back to circles", packName);
                return new ParametricShapeRenderer(ShapeDefinitions.Circle, "circles");
            }
            var packJson = Path.Combine(packDirFull, "pack.json");
            var loaded = _packLoader.LoadPack(packDirFull, packJson);
            if (loaded is not null)
            {
                var renderer = new PackIconRenderer(loaded, _loggerFactory.CreateLogger<PackIconRenderer>());
                if (renderer.IsHealthy)
                {
                    _loggerFactory.CreateLogger<TrayIconRendererFactory>().LogInformation(
                        "Loaded graphics pack {PackName}", packName);
                    return renderer;
                }
                renderer.Dispose();
            }
            _loggerFactory.CreateLogger<TrayIconRendererFactory>().LogWarning(
                "Graphics pack {PackName} failed to load — falling back to circles", packName);
            return new ParametricShapeRenderer(ShapeDefinitions.Circle, "circles");
        }

        return canonical switch
        {
            "squares" => new ParametricShapeRenderer(ShapeDefinitions.Square, canonical),
            "triangles" => new ParametricShapeRenderer(ShapeDefinitions.Triangle, canonical),
            "diamonds" => new ParametricShapeRenderer(ShapeDefinitions.Diamond, canonical),
            "hexagons" => new ParametricShapeRenderer(ShapeDefinitions.Hexagon, canonical),
            "plus" => new ParametricShapeRenderer(ShapeDefinitions.Plus, canonical),
            // "circles" and any unknown style both produce circles
            _ => new ParametricShapeRenderer(ShapeDefinitions.Circle, "circles"),
        };
    }
}
