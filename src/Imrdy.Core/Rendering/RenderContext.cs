using Microsoft.Extensions.Logging;

namespace Imrdy.Core.Rendering;

/// <summary>
/// Carries the inputs for a single render call. Passed to <see cref="IRenderableSurface.Render"/>.
/// </summary>
/// <param name="Args">Component-specific tokens after the component name.</param>
/// <param name="OutputPath">Absolute path for the output artifact; parent directory is pre-created by the caller.</param>
/// <param name="LoggerFactory">Logger factory for the render call; use <c>NullLoggerFactory.Instance</c> in tests.</param>
/// <param name="RepoRoot">
/// Absolute path to the dev-build repo root, read from <c>~/.imrdy/.dev-build</c>.
/// <c>null</c> when the marker file is absent (i.e., not a dev build).
/// </param>
public sealed record RenderContext(
    string[] Args,
    string OutputPath,
    ILoggerFactory LoggerFactory,
    string? RepoRoot);
