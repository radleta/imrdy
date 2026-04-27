namespace Imrdy.Core.Diagnostics;

/// <summary>
/// Top-level IPC response envelope for both render-live and inspect-live verbs.
/// </summary>
/// <param name="SchemaVersion">Schema version string, e.g. <c>"v1"</c>.</param>
/// <param name="Verb">Echo of the request verb (<c>"render-live"</c> or <c>"inspect-live"</c>).</param>
/// <param name="Error">Non-null when the request failed; null on success.</param>
/// <param name="Render">Populated for <c>render-live</c> success responses; null otherwise.</param>
/// <param name="Inspect">Populated for <c>inspect-live</c> success responses; null otherwise.</param>
public record InspectResponse(
    string SchemaVersion,
    string Verb,
    string? Error,
    RenderResult? Render,
    InspectResult? Inspect);

/// <summary>
/// Render artifact metadata returned by render-live.
/// </summary>
/// <param name="Width">Width of the rendered PNG in pixels.</param>
/// <param name="Height">Height of the rendered PNG in pixels.</param>
/// <param name="OutputPath">Absolute path to the written PNG file.</param>
public record RenderResult(int Width, int Height, string OutputPath);

/// <summary>
/// Full layout tree and diagnostic analysis returned by inspect-live.
/// </summary>
/// <param name="Form">Screen geometry of the inspected form.</param>
/// <param name="Tree">
/// Flat list of all controls in the form, depth-first pre-order.
/// <see cref="LayoutNode.ChildIndexes"/> reference positions within this list.
/// </param>
/// <param name="Diagnostics">Zero or more findings from the layout analyzer.</param>
/// <param name="DiagnosticTimestamp">ISO 8601 UTC timestamp of when the analysis was captured.</param>
public record InspectResult(
    FormGeometry Form,
    IReadOnlyList<LayoutNode> Tree,
    IReadOnlyList<DiagnosticFinding> Diagnostics,
    string DiagnosticTimestamp);
