using Imrdy.Core;
using Imrdy.Core.Graphics;
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
        iconStyle ??= "dots";
        if (iconStyle.StartsWith("pack:", StringComparison.OrdinalIgnoreCase))
        {
            var packName = iconStyle.Substring("pack:".Length);
            var graphicsPacksDirFull = Path.GetFullPath(ImrdyPaths.GraphicsPacksDir);
            var packDirFull = Path.GetFullPath(Path.Combine(graphicsPacksDirFull, packName));
            if (!packDirFull.StartsWith(graphicsPacksDirFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                _loggerFactory.CreateLogger<TrayIconRendererFactory>().LogWarning(
                    "Invalid pack name {PackName} — path traversal detected, falling back to dots", packName);
                return new CircleIconRenderer();
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
                "Graphics pack {PackName} failed to load — falling back to dots", packName);
        }
        return new CircleIconRenderer();
    }
}
