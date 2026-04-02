using System.Text.Json;

namespace Imrdy.Core.Validation;

/// <summary>
/// Validates ~/.claude/sounds/config.json: valid JSON, known keys, pack references resolve.
/// </summary>
public sealed class ConfigValidator
{
    private static readonly HashSet<string> KnownKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "default",
        "projectMappings",
        "soundEnabled",
    };

    /// <summary>
    /// Validates a sound config file.
    /// </summary>
    /// <param name="configPath">Path to config.json.</param>
    /// <param name="availablePackNames">Names of installed packs (for reference checking).</param>
    public ValidationResult Validate(string configPath, IReadOnlyCollection<string> availablePackNames)
    {
        var errors = new List<ValidationError>();

        if (!File.Exists(configPath))
        {
            errors.Add(new ValidationError(configPath, "Config file not found.", ValidationSeverity.Warning));
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

            var packNameSet = new HashSet<string>(availablePackNames, StringComparer.OrdinalIgnoreCase);

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (!KnownKeys.Contains(property.Name))
                {
                    errors.Add(new ValidationError(
                        $"{configPath} → {property.Name}",
                        $"Unknown key: '{property.Name}' (possible typo).",
                        ValidationSeverity.Warning));
                }
            }

            // Validate default pack reference
            if (doc.RootElement.TryGetProperty("default", out var defaultProp)
                && defaultProp.ValueKind == JsonValueKind.String)
            {
                var defaultPack = defaultProp.GetString();
                if (!string.IsNullOrEmpty(defaultPack) && !packNameSet.Contains(defaultPack))
                {
                    errors.Add(new ValidationError(
                        $"{configPath} → default",
                        $"Default pack '{defaultPack}' is not installed.",
                        ValidationSeverity.Error));
                }
            }

            // Validate project mapping pack references
            if (doc.RootElement.TryGetProperty("projectMappings", out var mappingsProp)
                && mappingsProp.ValueKind == JsonValueKind.Object)
            {
                foreach (var mapping in mappingsProp.EnumerateObject())
                {
                    if (mapping.Value.ValueKind == JsonValueKind.String)
                    {
                        var packName = mapping.Value.GetString();
                        if (!string.IsNullOrEmpty(packName) && !packNameSet.Contains(packName))
                        {
                            errors.Add(new ValidationError(
                                $"{configPath} → projectMappings.{mapping.Name}",
                                $"Pack '{packName}' referenced by project '{mapping.Name}' is not installed.",
                                ValidationSeverity.Error));
                        }
                    }
                }
            }
        }

        return new ValidationResult { Errors = errors };
    }
}
