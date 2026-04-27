namespace Imrdy.Core.Rendering;

/// <summary>
/// The outcome of a single <see cref="IRenderableSurface.Render"/> call.
/// </summary>
/// <param name="Success">Whether the render produced a usable artifact.</param>
/// <param name="Error">
/// Human-readable error message on failure; conventionally <c>null</c> on success,
/// but callers must not rely on the type enforcing that invariant.
/// </param>
/// <param name="Width">
/// Width of the rendered output in pixels. <c>0</c> for non-bitmap outputs
/// (e.g., JSON menu trees produced by a future <c>menu</c> component).
/// </param>
/// <param name="Height">
/// Height of the rendered output in pixels. <c>0</c> for non-bitmap outputs
/// (e.g., JSON menu trees produced by a future <c>menu</c> component).
/// </param>
public sealed record RenderResult(
    bool Success,
    string? Error,
    int Width,
    int Height);
