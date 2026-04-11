namespace Imrdy.Core.Icons;

public static class StyleNames
{
    public static IReadOnlyList<string> BuiltInStyles { get; } =
        ["circles", "squares", "triangles", "diamonds", "hexagons", "plus"];

    /// <summary>
    /// Normalizes a style name: maps "dots" (case-insensitive) to "circles",
    /// empty string or null to null, and passes all other values through unchanged.
    /// </summary>
    public static string? NormalizeStyleName(string? style)
    {
        if (string.IsNullOrEmpty(style))
        {
            return null;
        }

        if (style.Equals("dots", StringComparison.OrdinalIgnoreCase))
        {
            return "circles";
        }

        return style;
    }
}
