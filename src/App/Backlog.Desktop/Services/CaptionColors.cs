namespace Backlog.Desktop.Services;

/// <summary>
/// The dark tokens of <c>.devbook/design/color-scheme.md</c> a popped-out window's
/// title bar is drawn in (local ADR 0043 §4): the bar, its toggles and the system
/// caption buttons beside them. One place, so a token change is one edit here and
/// the caption cannot drift from itself. The product is dark only.
/// </summary>
internal static class CaptionColors
{
    /// <summary><c>color-background</c>: the bar, the same as the page under it.</summary>
    public static readonly Color Background = Color.FromArgb("#121214");

    /// <summary><c>color-background-alt</c>: a toggle or caption button under the pointer.</summary>
    public static readonly Color BackgroundAlt = Color.FromArgb("#202023");

    /// <summary><c>color-background-raised</c>: a toggle or caption button pressed down.</summary>
    public static readonly Color BackgroundRaised = Color.FromArgb("#353539");

    /// <summary><c>color-text-primary</c>: the title and the glyphs of an active window,
    /// and a pressed toggle's glyph.</summary>
    public static readonly Color TextPrimary = Color.FromArgb("#F8F9FA");

    /// <summary><c>color-text-secondary</c>: the title and the glyphs of an inactive
    /// window, and an unpressed toggle's glyph.</summary>
    public static readonly Color TextSecondary = Color.FromArgb("#CED4DA");

    /// <summary><c>color-primary</c>: a pressed toggle's edge, and its fill at a fraction.</summary>
    public static readonly Color Primary = Color.FromArgb("#F2C14E");

#if WINDOWS
    /// <summary><paramref name="color"/> as WinUI takes it, at <paramref name="alpha"/>
    /// when given — a pressed toggle's fill is <see cref="Primary"/> at 16%.</summary>
    public static Windows.UI.Color ToWinUI(Color color, byte? alpha = null)
    {
        color.ToRgba(out var red, out var green, out var blue, out var opaque);
        return Microsoft.UI.ColorHelper.FromArgb(alpha ?? opaque, red, green, blue);
    }
#endif
}
