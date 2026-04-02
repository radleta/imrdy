using System.Text.Json;

namespace Imrdy.Core.Validation;

/// <summary>
/// Validates ~/.imrdy/workspaces.json: valid JSON, required fields per entry, paths exist on disk.
/// </summary>
public sealed class WorkspaceValidator
{
    /// <summary>
    /// Validates a workspaces config file.
    /// </summary>
    public ValidationResult Validate(string configPath)
    {
        var errors = new List<ValidationError>();

        if (!File.Exists(configPath))
        {
            errors.Add(new ValidationError(configPath, "Workspaces file not found.", ValidationSeverity.Warning));
            return new ValidationResult { Errors = errors };
        }

        JsonDocument doc;
        try
        {
            var bytes = File.ReadAllBytes(configPath);
            doc = JsonDocument.Parse(bytes);
        }
        catch (JsonException ex)
        {
            errors.Add(new ValidationError(configPath, $"Invalid JSON: {ex.Message}", ValidationSeverity.Error));
            return new ValidationResult { Errors = errors };
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                errors.Add(new ValidationError(configPath, "Root element must be a JSON object.", ValidationSeverity.Error));
                return new ValidationResult { Errors = errors };
            }

            if (!doc.RootElement.TryGetProperty("workspaces", out var workspacesProp))
            {
                errors.Add(new ValidationError(configPath, "Missing required 'workspaces' array.", ValidationSeverity.Error));
                return new ValidationResult { Errors = errors };
            }

            if (workspacesProp.ValueKind != JsonValueKind.Array)
            {
                errors.Add(new ValidationError(configPath, "'workspaces' must be a JSON array.", ValidationSeverity.Error));
                return new ValidationResult { Errors = errors };
            }

            var index = 0;
            foreach (var entry in workspacesProp.EnumerateArray())
            {
                var entryPath = $"{configPath} → workspaces[{index}]";

                if (entry.ValueKind != JsonValueKind.Object)
                {
                    errors.Add(new ValidationError(entryPath, "Workspace entry must be a JSON object.", ValidationSeverity.Error));
                    index++;
                    continue;
                }

                // Required field: path
                var hasPath = entry.TryGetProperty("path", out var pathProp)
                              && pathProp.ValueKind == JsonValueKind.String
                              && !string.IsNullOrWhiteSpace(pathProp.GetString());
                if (!hasPath)
                {
                    errors.Add(new ValidationError(entryPath, "Missing required field: 'path'.", ValidationSeverity.Error));
                }

                // Required field: name
                var hasName = entry.TryGetProperty("name", out var nameProp)
                              && nameProp.ValueKind == JsonValueKind.String
                              && !string.IsNullOrWhiteSpace(nameProp.GetString());
                if (!hasName)
                {
                    errors.Add(new ValidationError(entryPath, "Missing required field: 'name'.", ValidationSeverity.Error));
                }

                // Required field: desktop
                var hasDesktop = entry.TryGetProperty("desktop", out var desktopProp)
                                 && desktopProp.ValueKind == JsonValueKind.Number;
                if (!hasDesktop)
                {
                    errors.Add(new ValidationError(entryPath, "Missing required field: 'desktop'.", ValidationSeverity.Error));
                }

                // Path exists on disk
                if (hasPath)
                {
                    var diskPath = pathProp.GetString()!;
                    if (!Directory.Exists(diskPath))
                    {
                        errors.Add(new ValidationError(entryPath, $"Path does not exist on disk: '{diskPath}'.", ValidationSeverity.Warning));
                    }
                }

                index++;
            }
        }

        return new ValidationResult { Errors = errors };
    }
}
