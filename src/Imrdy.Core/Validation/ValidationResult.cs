namespace Imrdy.Core.Validation;

/// <summary>
/// Severity level for a validation finding.
/// </summary>
public enum ValidationSeverity
{
    Warning,
    Error,
}

/// <summary>
/// A single validation finding with path context and actionable message.
/// </summary>
public sealed record ValidationError(string Path, string Message, ValidationSeverity Severity);

/// <summary>
/// Result of validating a configuration file.
/// </summary>
public sealed record ValidationResult
{
    public bool IsValid => Errors.Count == 0 || Errors.All(e => e.Severity == ValidationSeverity.Warning);
    public List<ValidationError> Errors { get; init; } = [];
}
