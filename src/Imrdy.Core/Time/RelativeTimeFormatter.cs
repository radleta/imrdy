namespace Imrdy.Core.Time;

/// <summary>
/// Platform-independent duration formatter shared by Core builders and Windows forms.
/// Algorithm is the canonical copy; <see cref="Imrdy.Windows.Dashboard.HoverDashboardFormBase"/>
/// delegates its <c>FormatDuration</c> helper here.
/// </summary>
public static class RelativeTimeFormatter
{
    /// <summary>
    /// Formats a duration as a compact human-readable string without trailing unit:
    /// "18s" | "2m" | "1h 14m" | "3d". Callers add contextual prefixes/suffixes
    /// (e.g. "for 18s", "idle 2m ago", "42m old").
    /// </summary>
    public static string FormatDuration(TimeSpan span)
    {
        if (span.TotalSeconds < 60) return $"{(int)span.TotalSeconds}s";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m";
        var h = (int)span.TotalHours;
        var m = (int)(span.TotalMinutes % 60);
        if (h < 24) return m > 0 ? $"{h}h {m}m" : $"{h}h";
        return $"{(int)span.TotalDays}d";
    }
}
