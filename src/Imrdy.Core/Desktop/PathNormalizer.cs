using System.Text.RegularExpressions;

namespace Imrdy.Core.Desktop;

/// <summary>
/// Normalizes file paths across MSYS, Windows, and mixed formats.
/// Provides case-insensitive comparison for path matching.
/// </summary>
public static partial class PathNormalizer
{
    /// <summary>
    /// Regex matching MSYS-style paths: /d/dev/... → D:\dev\...
    /// </summary>
    [GeneratedRegex(@"^/([a-zA-Z])(/.*)?$")]
    private static partial Regex MsysPathRegex();

    /// <summary>
    /// Normalizes a path from any format (MSYS, Windows, mixed) to a canonical Windows form.
    /// </summary>
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        // Convert MSYS /d/dev/... → D:\dev\...
        var msysMatch = MsysPathRegex().Match(path);
        if (msysMatch.Success)
        {
            var driveLetter = msysMatch.Groups[1].Value.ToUpperInvariant();
            var rest = msysMatch.Groups[2].Success ? msysMatch.Groups[2].Value : "";
            // Use backslash path to avoid GetFullPath resolving "D:" as CWD on that drive
            path = $@"{driveLetter}:\{rest.TrimStart('/')}";
        }

        // Normalize via Path.GetFullPath (handles forward slashes, trailing slashes, etc.)
        path = Path.GetFullPath(path);

        // Remove trailing directory separator, but preserve drive root (e.g., "D:\")
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        // Drive root like "D:" needs the trailing separator back
        if (trimmed.Length == 2 && trimmed[1] == ':')
        {
            return trimmed + Path.DirectorySeparatorChar;
        }

        return trimmed;
    }

    /// <summary>
    /// Compares two paths for equality after normalization (case-insensitive on Windows).
    /// </summary>
    public static bool AreEqual(string path1, string path2)
    {
        return string.Equals(Normalize(path1), Normalize(path2), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Derives a project name from a path (last directory component).
    /// Port of deriveProject() from hook-lib.mjs.
    /// </summary>
    public static string DeriveProject(string path)
    {
        var normalized = Normalize(path);
        if (string.IsNullOrEmpty(normalized))
        {
            return "";
        }

        return Path.GetFileName(normalized) ?? "";
    }
}
