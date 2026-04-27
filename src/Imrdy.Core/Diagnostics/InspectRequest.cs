namespace Imrdy.Core.Diagnostics;

/// <summary>
/// Single request type for both render-live and inspect-live verbs.
/// OutputPath is null for inspect-live; set to a file path for render-live.
/// </summary>
public record InspectRequest(string Verb, string SessionId, string? OutputPath);
