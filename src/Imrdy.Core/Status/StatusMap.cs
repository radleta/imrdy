namespace Imrdy.Core.Status;

/// <summary>
/// Two-layer status mapping: hook event → base status → RGB color.
/// Port of PS1 Resolve-StatusColor.
/// </summary>
public static class StatusMap
{
    private static readonly Dictionary<string, string> HookToBase = new(StringComparer.OrdinalIgnoreCase)
    {
        ["start"] = "busy",
        ["end"] = "unknown",
    };

    private static readonly Dictionary<string, (byte R, byte G, byte B)> BaseToColor = new(StringComparer.OrdinalIgnoreCase)
    {
        ["busy"] = (230, 40, 40),
        ["idle"] = (40, 200, 40),
        ["attention"] = (255, 120, 0),
        ["permission"] = (180, 60, 230),
        ["compact"] = (60, 120, 230),
        ["unknown"] = (128, 128, 128),
        ["workspace"] = (255, 255, 255),
    };

    private static readonly (byte R, byte G, byte B) DefaultColor = (128, 128, 128);

    /// <summary>
    /// Resolves a hook status to its base status.
    /// If the status has no mapping, it passes through as-is.
    /// </summary>
    public static string ResolveBaseStatus(string hookStatus)
    {
        return HookToBase.TryGetValue(hookStatus, out var baseStatus) ? baseStatus : hookStatus;
    }

    /// <summary>
    /// Resolves a hook status to its RGB color through two-layer lookup.
    /// </summary>
    public static (byte R, byte G, byte B) ResolveColor(string hookStatus)
    {
        var baseStatus = ResolveBaseStatus(hookStatus);
        return BaseToColor.TryGetValue(baseStatus, out var color) ? color : DefaultColor;
    }

    /// <summary>
    /// Gets all known base status names.
    /// </summary>
    public static IReadOnlyCollection<string> KnownBaseStatuses => BaseToColor.Keys;
}
