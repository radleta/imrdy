using System.Text.Json;

namespace Imrdy.Core.Validation;

/// <summary>
/// Validates ~/.imrdy/config.json: valid JSON, known keys, pack references resolve.
/// </summary>
public sealed class ConfigValidator
{
    private static readonly HashSet<string> KnownRootKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "tray",
        "sound",
    };

    private static readonly HashSet<string> KnownTrayKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "enabled",
    };

    private static readonly HashSet<string> KnownSoundKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "enabled",
        "defaultPack",
        "disabledPacks",
        "projects",
    };

    /// <summary>
    /// Validates a config file.
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

            // Check for unknown top-level keys
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (!KnownRootKeys.Contains(property.Name))
                {
                    errors.Add(new ValidationError(
                        $"{configPath} → {property.Name}",
                        $"Unknown key: '{property.Name}' (possible typo).",
                        ValidationSeverity.Warning));
                }
            }

            // Validate "tray" section
            if (doc.RootElement.TryGetProperty("tray", out var trayProp))
            {
                if (trayProp.ValueKind != JsonValueKind.Object)
                {
                    errors.Add(new ValidationError(
                        $"{configPath} → tray",
                        "'tray' must be a JSON object.",
                        ValidationSeverity.Error));
                }
                else
                {
                    foreach (var prop in trayProp.EnumerateObject())
                    {
                        if (!KnownTrayKeys.Contains(prop.Name))
                        {
                            errors.Add(new ValidationError(
                                $"{configPath} → tray.{prop.Name}",
                                $"Unknown key: 'tray.{prop.Name}' (possible typo).",
                                ValidationSeverity.Warning));
                        }
                    }
                }
            }

            // Validate "sound" section
            if (doc.RootElement.TryGetProperty("sound", out var soundProp))
            {
                if (soundProp.ValueKind != JsonValueKind.Object)
                {
                    errors.Add(new ValidationError(
                        $"{configPath} → sound",
                        "'sound' must be a JSON object.",
                        ValidationSeverity.Error));
                }
                else
                {
                    foreach (var prop in soundProp.EnumerateObject())
                    {
                        if (!KnownSoundKeys.Contains(prop.Name))
                        {
                            errors.Add(new ValidationError(
                                $"{configPath} → sound.{prop.Name}",
                                $"Unknown key: 'sound.{prop.Name}' (possible typo).",
                                ValidationSeverity.Warning));
                        }
                    }

                    // Validate defaultPack reference
                    if (soundProp.TryGetProperty("defaultPack", out var defaultProp)
                        && defaultProp.ValueKind == JsonValueKind.String)
                    {
                        var defaultPack = defaultProp.GetString();
                        if (!string.IsNullOrEmpty(defaultPack)
                            && !string.Equals(defaultPack, "random", StringComparison.OrdinalIgnoreCase)
                            && !packNameSet.Contains(defaultPack))
                        {
                            errors.Add(new ValidationError(
                                $"{configPath} → sound.defaultPack",
                                $"Default pack '{defaultPack}' is not installed.",
                                ValidationSeverity.Error));
                        }
                    }

                    // Validate projects pack references
                    if (soundProp.TryGetProperty("projects", out var projectsProp)
                        && projectsProp.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var mapping in projectsProp.EnumerateObject())
                        {
                            if (mapping.Value.ValueKind == JsonValueKind.String)
                            {
                                var packName = mapping.Value.GetString();
                                if (!string.IsNullOrEmpty(packName) && !packNameSet.Contains(packName))
                                {
                                    errors.Add(new ValidationError(
                                        $"{configPath} → sound.projects.{mapping.Name}",
                                        $"Pack '{packName}' referenced by project '{mapping.Name}' is not installed.",
                                        ValidationSeverity.Error));
                                }
                            }
                        }
                    }
                }
            }
        }

        return new ValidationResult { Errors = errors };
    }
}
