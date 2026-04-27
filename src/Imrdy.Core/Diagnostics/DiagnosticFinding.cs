using System.Collections.Immutable;

namespace Imrdy.Core.Diagnostics;

/// <summary>
/// A single layout diagnostic finding produced by the analyzer.
/// </summary>
/// <param name="Kind">
/// Category: <c>regionClipRisk</c> | <c>siblingOverlap</c> | <c>edgeProximity</c> | <c>collapsedRow</c>
/// </param>
/// <param name="Severity">
/// <c>info</c> | <c>warning</c> | <c>error</c>
/// </param>
/// <param name="ControlPath">Slash-separated control path from form root to the offending control (e.g. <c>DashboardForm/Panel[header]/Label[title]</c>).</param>
/// <param name="Message">Human-readable description of the finding.</param>
/// <param name="Details">
/// Non-nullable key-value map of supplementary data (e.g., pixel measurements, thresholds).
/// Always an empty dictionary — never null — when no extra data is present.
/// </param>
public record DiagnosticFinding(
    string Kind,
    string Severity,
    string ControlPath,
    string Message,
    IReadOnlyDictionary<string, string> Details)
{
    public DiagnosticFinding(string Kind, string Severity, string ControlPath, string Message)
        : this(Kind, Severity, ControlPath, Message, ImmutableDictionary<string, string>.Empty)
    {
    }
}
