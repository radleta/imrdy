using System.Text.Json;
using Imrdy.Core.Sound;

namespace Imrdy.Core.Validation;

/// <summary>
/// Validates a pack.json file: required fields, event folders exist, WAV files present.
/// </summary>
public sealed class PackValidator
{
    /// <summary>
    /// Validates a pack at the given directory path.
    /// </summary>
    public ValidationResult Validate(string packDir)
    {
        var errors = new List<ValidationError>();
        var packJsonPath = Path.Combine(packDir, "pack.json");

        if (!File.Exists(packJsonPath))
        {
            errors.Add(new ValidationError(packJsonPath, "pack.json not found.", ValidationSeverity.Error));
            return new ValidationResult { Errors = errors };
        }

        PackJson? packJson;
        try
        {
            var bytes = File.ReadAllBytes(packJsonPath);
            packJson = JsonSerializer.Deserialize(bytes, ImrdyJsonContext.Default.PackJson);
        }
        catch (JsonException ex)
        {
            errors.Add(new ValidationError(packJsonPath, $"Invalid JSON: {ex.Message}", ValidationSeverity.Error));
            return new ValidationResult { Errors = errors };
        }

        if (packJson is null)
        {
            errors.Add(new ValidationError(packJsonPath, "pack.json deserialized to null.", ValidationSeverity.Error));
            return new ValidationResult { Errors = errors };
        }

        // Required fields
        if (string.IsNullOrWhiteSpace(packJson.Name))
        {
            errors.Add(new ValidationError(packJsonPath, "Missing required field: name.", ValidationSeverity.Error));
        }

        if (string.IsNullOrWhiteSpace(packJson.Description))
        {
            errors.Add(new ValidationError(packJsonPath, "Missing required field: description.", ValidationSeverity.Error));
        }

        if (string.IsNullOrWhiteSpace(packJson.Version))
        {
            errors.Add(new ValidationError(packJsonPath, "Missing required field: version.", ValidationSeverity.Error));
        }

        // Validate events
        if (packJson.Events.Count == 0)
        {
            errors.Add(new ValidationError(packJsonPath, "No events defined.", ValidationSeverity.Warning));
        }

        foreach (var (eventKey, eventConfig) in packJson.Events)
        {
            var eventPath = $"{packJsonPath} → events.{eventKey}";

            // Check if event name is recognized
            var soundEvent = SoundEventExtensions.FromFolderName(eventKey);
            if (soundEvent is null)
            {
                errors.Add(new ValidationError(eventPath, $"Unknown event name: '{eventKey}'.", ValidationSeverity.Warning));
                continue;
            }

            // Check folder property
            if (string.IsNullOrWhiteSpace(eventConfig.Folder))
            {
                errors.Add(new ValidationError(eventPath, "Missing 'folder' property.", ValidationSeverity.Error));
                continue;
            }

            // Check folder exists
            var folderPath = Path.Combine(packDir, eventConfig.Folder);
            if (!Directory.Exists(folderPath))
            {
                errors.Add(new ValidationError(folderPath, $"Event folder does not exist: '{eventConfig.Folder}'.", ValidationSeverity.Error));
                continue;
            }

            // Check for WAV files
            var wavFiles = Directory.GetFiles(folderPath, "*.wav");
            if (wavFiles.Length == 0)
            {
                errors.Add(new ValidationError(folderPath, $"No .wav files found in folder: '{eventConfig.Folder}'.", ValidationSeverity.Error));
                continue;
            }

            // Check WAV files are non-zero size
            foreach (var wavFile in wavFiles)
            {
                var fileInfo = new FileInfo(wavFile);
                if (fileInfo.Length == 0)
                {
                    errors.Add(new ValidationError(wavFile, "WAV file is empty (zero bytes).", ValidationSeverity.Error));
                }
            }
        }

        return new ValidationResult { Errors = errors };
    }
}
