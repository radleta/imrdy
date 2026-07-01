namespace Imrdy.Core.Overlay;

public enum HorizontalAnchor { Left, Center, Right }

public enum VerticalAnchor { Top, Bottom }

/// <summary>
/// Pure, WinForms-free representation of one of the six snap anchors.
/// Single source of truth for config-string &lt;-&gt; enum-pair conversion.
/// </summary>
public readonly record struct OverlayAnchor(HorizontalAnchor Horizontal, VerticalAnchor Vertical)
{
    private static readonly (string Key, HorizontalAnchor H, VerticalAnchor V)[] _map =
    [
        ("top-left",      HorizontalAnchor.Left,   VerticalAnchor.Top),
        ("top-center",    HorizontalAnchor.Center, VerticalAnchor.Top),
        ("top-right",     HorizontalAnchor.Right,  VerticalAnchor.Top),
        ("bottom-left",   HorizontalAnchor.Left,   VerticalAnchor.Bottom),
        ("bottom-center", HorizontalAnchor.Center, VerticalAnchor.Bottom),
        ("bottom-right",  HorizontalAnchor.Right,  VerticalAnchor.Bottom),
    ];

    private static readonly OverlayAnchor Default = new(HorizontalAnchor.Right, VerticalAnchor.Bottom);

    /// <summary>
    /// Maps a config position string (e.g. "bottom-right") to its enum pair.
    /// Case-insensitive. Unknown, blank, or null input returns <c>(Right, Bottom)</c>.
    /// Never throws.
    /// </summary>
    public static OverlayAnchor Parse(string? position)
    {
        if (string.IsNullOrWhiteSpace(position))
            return Default;

        var lower = position.ToLowerInvariant();
        foreach (var (key, h, v) in _map)
            if (key == lower)
                return new OverlayAnchor(h, v);

        return Default;
    }

    /// <summary>
    /// Emits the canonical lowercase hyphenated config string (e.g. "bottom-right").
    /// Round-trips with <see cref="Parse"/>.
    /// </summary>
    public string ToConfigString()
    {
        var h = Horizontal switch
        {
            HorizontalAnchor.Left   => "left",
            HorizontalAnchor.Center => "center",
            _                       => "right",
        };
        var v = Vertical switch
        {
            VerticalAnchor.Top => "top",
            _                  => "bottom",
        };
        return $"{v}-{h}";
    }
}
